from __future__ import annotations

from collections import deque
from dataclasses import dataclass
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
from typing import Any

import tomllib

from .diagnostics import YasnyError


CONFIG_NAMES = ("yasn.toml", "yasny.toml")
DEPS_DIR_REL = Path(".yasn") / "deps"
LOCK_FILE_REL = Path(".yasn") / "deps.lock.json"


@dataclass(slots=True)
class DependencySpec:
    name: str
    kind: str  # "git" | "path"
    source: str
    ref: str | None = None


@dataclass(slots=True)
class DependenciesManifest:
    project_root: Path
    config_path: Path
    deps_root: Path
    lock_path: Path
    specs: list[DependencySpec]


@dataclass(slots=True)
class DependencyInstallResult:
    spec: DependencySpec
    target: Path
    resolved: str
    direct: bool
    requested_by: str | None = None


@dataclass(slots=True)
class DependencyStatus:
    spec: DependencySpec
    target: Path
    installed: bool
    resolved: str | None = None
    direct: bool = True
    requested_by: str | None = None


@dataclass(slots=True)
class _QueuedDependency:
    spec: DependencySpec
    project_root: Path
    direct: bool
    requested_by: str | None = None


@dataclass(slots=True)
class _LockDependency:
    spec: DependencySpec
    target: Path
    resolved: str | None
    direct: bool
    requested_by: str | None


def load_manifest(cwd: Path | None = None) -> DependenciesManifest:
    project_root, config_path = _find_project_context(cwd or Path.cwd())
    data = _read_toml(config_path)
    specs = _parse_dependencies(data, config_path)
    deps_root = project_root / DEPS_DIR_REL
    lock_path = project_root / LOCK_FILE_REL
    return DependenciesManifest(
        project_root=project_root,
        config_path=config_path,
        deps_root=deps_root,
        lock_path=lock_path,
        specs=specs,
    )


def install_dependencies(cwd: Path | None = None, clean: bool = False) -> tuple[DependenciesManifest, list[DependencyInstallResult]]:
    manifest = load_manifest(cwd)
    manifest.deps_root.mkdir(parents=True, exist_ok=True)

    queue: deque[_QueuedDependency] = deque(
        _QueuedDependency(spec=spec, project_root=manifest.project_root, direct=True, requested_by=None)
        for spec in manifest.specs
    )
    # name -> (identity, source meta)
    planned: dict[str, tuple[tuple[str, str, str | None], _QueuedDependency]] = {}
    results: list[DependencyInstallResult] = []

    while queue:
        dep = queue.popleft()
        identity = _dependency_identity(dep.spec, dep.project_root)
        existing = planned.get(dep.spec.name)
        if existing is not None:
            prev_identity, prev_dep = existing
            if prev_identity != identity:
                prev_source = _dependency_source_for_error(prev_dep.spec, prev_dep.project_root)
                new_source = _dependency_source_for_error(dep.spec, dep.project_root)
                raise YasnyError(
                    (
                        f"Конфликт зависимостей '{dep.spec.name}': "
                        f"{prev_source} и {new_source}. "
                        "Используйте единый источник и версию."
                    ),
                    path=str(manifest.config_path),
                )
            continue

        planned[dep.spec.name] = (identity, dep)
        target = manifest.deps_root / dep.spec.name
        if dep.spec.kind == "path":
            resolved, nested_root = _install_from_path(dep.spec, dep.project_root, target)
        elif dep.spec.kind == "git":
            resolved, nested_root = _install_from_git(dep.spec, dep.project_root, target)
        else:
            raise YasnyError(f"Неподдерживаемый тип зависимости: {dep.spec.kind}", path=str(manifest.config_path))

        results.append(
            DependencyInstallResult(
                spec=dep.spec,
                target=target,
                resolved=resolved,
                direct=dep.direct,
                requested_by=dep.requested_by,
            )
        )

        for nested in _load_nested_dependency_specs(nested_root):
            queue.append(
                _QueuedDependency(
                    spec=nested,
                    project_root=nested_root,
                    direct=False,
                    requested_by=dep.spec.name,
                )
            )

    if clean:
        wanted = {item.spec.name for item in results}
        for path in manifest.deps_root.iterdir():
            if not path.is_dir():
                continue
            if path.name in wanted:
                continue
            _remove_tree(path)

    _write_lock(manifest, results)
    return manifest, results


def list_dependencies(
    cwd: Path | None = None,
    include_transitive: bool = False,
) -> tuple[DependenciesManifest, list[DependencyStatus]]:
    manifest = load_manifest(cwd)
    statuses: list[DependencyStatus] = []
    for spec in manifest.specs:
        target = manifest.deps_root / spec.name
        installed = target.exists()
        resolved: str | None = None
        if installed:
            if spec.kind == "git":
                resolved = _git_head(target)
            else:
                resolved = str(_resolve_dep_path(spec.source, manifest.project_root))
        statuses.append(
            DependencyStatus(
                spec=spec,
                target=target,
                installed=installed,
                resolved=resolved,
                direct=True,
                requested_by=None,
            )
        )

    if include_transitive:
        seen = {item.spec.name for item in statuses}
        for locked in _read_lock_dependencies(manifest):
            if locked.spec.name in seen:
                continue
            installed = locked.target.exists()
            resolved = locked.resolved
            if installed and resolved is None and locked.spec.kind == "git":
                resolved = _git_head(locked.target)
            statuses.append(
                DependencyStatus(
                    spec=locked.spec,
                    target=locked.target,
                    installed=installed,
                    resolved=resolved,
                    direct=locked.direct,
                    requested_by=locked.requested_by,
                )
            )
            seen.add(locked.spec.name)
        statuses.sort(key=lambda item: (not item.direct, item.spec.name))
    return manifest, statuses


def _find_project_context(start: Path) -> tuple[Path, Path]:
    root = start.resolve()
    search = [root, *root.parents]
    for cur in search:
        cfg = _find_config_file_in_dir(cur)
        if cfg is not None:
            return cur, cfg
    raise YasnyError("Не найден yasn.toml/yasny.toml. Зависимости требуют конфиг проекта.")


def _find_config_file_in_dir(directory: Path) -> Path | None:
    for cfg_name in CONFIG_NAMES:
        cfg = directory / cfg_name
        if cfg.exists():
            return cfg
    return None


def _read_toml(path: Path) -> dict[str, Any]:
    try:
        return tomllib.loads(path.read_text(encoding="utf-8-sig"))
    except Exception as exc:  # noqa: BLE001
        raise YasnyError(f"Не удалось прочитать {path.name}: {exc}", path=str(path)) from exc


def _parse_dependencies(data: dict[str, Any], config_path: Path) -> list[DependencySpec]:
    deps_raw = data.get("dependencies", {})
    if deps_raw is None:
        deps_raw = {}
    if not isinstance(deps_raw, dict):
        raise YasnyError("Секция [dependencies] должна быть объектом", path=str(config_path))

    specs: list[DependencySpec] = []
    seen: set[str] = set()
    for name, value in deps_raw.items():
        if not isinstance(name, str) or not name.strip():
            raise YasnyError("Имя зависимости должно быть непустой строкой", path=str(config_path))
        dep_name = name.strip()
        if dep_name in seen:
            raise YasnyError(f"Повтор имени зависимости: {dep_name}", path=str(config_path))
        seen.add(dep_name)

        source: str
        ref: str | None = None
        if isinstance(value, str):
            source = value.strip()
        elif isinstance(value, dict):
            raw_source = value.get("source")
            raw_ref = value.get("ref")
            if not isinstance(raw_source, str) or not raw_source.strip():
                raise YasnyError(f"dependencies.{dep_name}.source должен быть непустой строкой", path=str(config_path))
            source = raw_source.strip()
            if raw_ref is not None:
                if not isinstance(raw_ref, str) or not raw_ref.strip():
                    raise YasnyError(f"dependencies.{dep_name}.ref должен быть непустой строкой", path=str(config_path))
                ref = raw_ref.strip()
        else:
            raise YasnyError(
                f"dependencies.{dep_name} должен быть строкой или объектом {{source, ref?}}",
                path=str(config_path),
            )

        kind, normalized_source, normalized_ref = _normalize_source(source, ref, config_path)
        specs.append(DependencySpec(name=dep_name, kind=kind, source=normalized_source, ref=normalized_ref))

    return specs


def _normalize_source(source: str, ref: str | None, config_path: Path) -> tuple[str, str, str | None]:
    if source.startswith("git+"):
        raw = source[4:].strip()
        if not raw:
            raise YasnyError("Пустой git-источник зависимости", path=str(config_path))
        if "#" in raw:
            base, hash_ref = raw.split("#", 1)
            if ref is None and hash_ref.strip():
                ref = hash_ref.strip()
            raw = base.strip()
        if not raw:
            raise YasnyError("Пустой URL git-источника зависимости", path=str(config_path))
        return "git", raw, ref

    if source.startswith("path:"):
        raw_path = source[5:].strip()
        if not raw_path:
            raise YasnyError("Пустой path-источник зависимости", path=str(config_path))
        return "path", raw_path, None

    # По умолчанию считаем локальным путём.
    return "path", source, None


def _install_from_path(spec: DependencySpec, project_root: Path, target: Path) -> tuple[str, Path]:
    source_path = _resolve_dep_path(spec.source, project_root)
    if not source_path.exists():
        raise YasnyError(f"Путь зависимости не найден: {source_path}")
    if not source_path.is_dir():
        raise YasnyError(f"Путь зависимости должен быть директорией: {source_path}")

    if target.exists():
        _remove_tree(target)
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(source_path, target, ignore=shutil.ignore_patterns(".git", "__pycache__", ".venv", "node_modules"))
    return str(source_path), source_path


def _resolve_dep_path(raw: str, project_root: Path) -> Path:
    p = Path(raw).expanduser()
    if p.is_absolute():
        return p.resolve()
    return (project_root / p).resolve()


def _install_from_git(spec: DependencySpec, project_root: Path, target: Path) -> tuple[str, Path]:
    source = spec.source
    if _is_local_git_source(source):
        source = str(_resolve_dep_path(source, project_root))
        if not Path(source).exists():
            raise YasnyError(f"Git-источник не найден: {source}")

    if target.exists():
        _remove_tree(target)
    target.parent.mkdir(parents=True, exist_ok=True)

    _run_cmd(["git", "clone", "--depth", "1", source, str(target)], cwd=target.parent)
    if spec.ref is not None:
        _run_cmd(["git", "fetch", "--depth", "1", "origin", spec.ref], cwd=target)
        _run_cmd(["git", "checkout", "FETCH_HEAD"], cwd=target)
    return _git_head(target), target


def _is_local_git_source(source: str) -> bool:
    src = source.strip()
    if "://" in src:
        return False
    if src.startswith("git@"):
        return False
    return True


def _git_head(repo_dir: Path) -> str:
    out = _run_cmd(["git", "rev-parse", "HEAD"], cwd=repo_dir)
    return out.strip()


def _run_cmd(cmd: list[str], cwd: Path) -> str:
    try:
        proc = subprocess.run(
            cmd,
            cwd=str(cwd),
            capture_output=True,
            text=True,
            check=False,
            encoding="utf-8",
            errors="replace",
        )
    except FileNotFoundError as exc:
        raise YasnyError(f"Не найдено внешнее приложение: {cmd[0]}") from exc

    if proc.returncode != 0:
        stderr = (proc.stderr or "").strip()
        stdout = (proc.stdout or "").strip()
        details = stderr or stdout or "неизвестная ошибка"
        raise YasnyError(f"Команда завершилась с ошибкой ({proc.returncode}): {' '.join(cmd)}\n{details}")
    return proc.stdout or ""


def _write_lock(manifest: DependenciesManifest, installs: list[DependencyInstallResult]) -> None:
    deps_payload = []
    for item in sorted(installs, key=lambda x: x.spec.name):
        deps_payload.append(
            {
                "name": item.spec.name,
                "kind": item.spec.kind,
                "source": item.spec.source,
                "ref": item.spec.ref,
                "resolved": item.resolved,
                "target": _serialize_target(item.target, manifest.project_root),
                "direct": item.direct,
                "requested_by": item.requested_by,
            }
        )

    payload = {
        "version": 1,
        "config": _serialize_target(manifest.config_path, manifest.project_root),
        "dependencies": deps_payload,
    }
    manifest.lock_path.parent.mkdir(parents=True, exist_ok=True)
    manifest.lock_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")


def _serialize_target(path: Path, project_root: Path) -> str:
    try:
        return str(path.resolve().relative_to(project_root.resolve()))
    except ValueError:
        return str(path.resolve())


def _read_lock_dependencies(manifest: DependenciesManifest) -> list[_LockDependency]:
    if not manifest.lock_path.exists():
        return []

    try:
        raw = json.loads(manifest.lock_path.read_text(encoding="utf-8-sig"))
    except Exception:  # noqa: BLE001
        return []

    items = raw.get("dependencies", [])
    if not isinstance(items, list):
        return []

    out: list[_LockDependency] = []
    for item in items:
        if not isinstance(item, dict):
            continue
        name = item.get("name")
        kind = item.get("kind")
        source = item.get("source")
        if not isinstance(name, str) or not isinstance(kind, str) or not isinstance(source, str):
            continue

        raw_ref = item.get("ref")
        ref = raw_ref if isinstance(raw_ref, str) and raw_ref.strip() else None

        raw_target = item.get("target")
        if isinstance(raw_target, str) and raw_target.strip():
            target = Path(raw_target)
            if not target.is_absolute():
                target = (manifest.project_root / target).resolve()
            else:
                target = target.resolve()
        else:
            target = (manifest.deps_root / name).resolve()

        raw_resolved = item.get("resolved")
        resolved = raw_resolved if isinstance(raw_resolved, str) and raw_resolved.strip() else None

        direct = bool(item.get("direct", False))
        raw_req = item.get("requested_by")
        requested_by = raw_req if isinstance(raw_req, str) and raw_req.strip() else None

        out.append(
            _LockDependency(
                spec=DependencySpec(name=name, kind=kind, source=source, ref=ref),
                target=target,
                resolved=resolved,
                direct=direct,
                requested_by=requested_by,
            )
        )
    return out


def _dependency_identity(spec: DependencySpec, project_root: Path) -> tuple[str, str, str | None]:
    if spec.kind == "path":
        return ("path", str(_resolve_dep_path(spec.source, project_root)), spec.ref)

    if spec.kind == "git":
        source = spec.source
        if _is_local_git_source(source):
            source = str(_resolve_dep_path(source, project_root))
        return ("git", source, spec.ref)

    return (spec.kind, spec.source, spec.ref)


def _dependency_source_for_error(spec: DependencySpec, project_root: Path) -> str:
    if spec.kind == "path":
        return f"path:{_resolve_dep_path(spec.source, project_root)}"
    if spec.kind == "git":
        source = spec.source
        if _is_local_git_source(source):
            source = str(_resolve_dep_path(source, project_root))
        ref = f"#{spec.ref}" if spec.ref else ""
        return f"git+{source}{ref}"
    return spec.source


def _load_nested_dependency_specs(dep_root: Path) -> list[DependencySpec]:
    config = _find_config_file_in_dir(dep_root)
    if config is None:
        return []
    data = _read_toml(config)
    return _parse_dependencies(data, config)


def _remove_tree(path: Path) -> None:
    if not path.exists():
        return
    shutil.rmtree(path, onerror=_remove_readonly)


def _remove_readonly(func: Any, raw_path: str, _exc: tuple[type[BaseException], BaseException, Any]) -> None:
    os.chmod(raw_path, stat.S_IWRITE)
    func(raw_path)
