from __future__ import annotations

import subprocess
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import tomllib

from .diagnostics import YasnError


@dataclass(slots=True)
class RunProfile:
    mode: str
    backend: str
    host: str
    port: int
    frontend_cmd: str | None
    frontend_cwd: str | None
    project_root: Path
    config_path: Path | None


def run_mode(mode: str, backend: str | None = None, host: str | None = None, port: int | None = None) -> int:
    profile = load_run_profile(mode=mode, backend=backend, host=host, port=port)

    backend_cmd = [
        sys.executable,
        "-m",
        "yasn",
        "serve",
        profile.backend,
        "--host",
        profile.host,
        "--port",
        str(profile.port),
    ]

    print(f"[yasn] Mode: {profile.mode}")
    if profile.config_path is not None:
        print(f"[yasn] Config: {profile.config_path}")
    print(f"[yasn] Backend: {' '.join(backend_cmd)}")

    backend_proc = subprocess.Popen(backend_cmd, cwd=str(profile.project_root))
    frontend_proc: subprocess.Popen[str] | None = None

    try:
        if profile.frontend_cmd:
            frontend_dir = _resolve_frontend_cwd(profile)
            print(f"[yasn] Frontend: {profile.frontend_cmd} (cwd={frontend_dir})")
            frontend_proc = subprocess.Popen(profile.frontend_cmd, cwd=str(frontend_dir), shell=True)

        if frontend_proc is None:
            return backend_proc.wait()

        while True:
            backend_rc = backend_proc.poll()
            frontend_rc = frontend_proc.poll()

            if backend_rc is not None:
                print(f"[yasn] Backend exited with code {backend_rc}")
                _stop_process(frontend_proc)
                return int(backend_rc)
            if frontend_rc is not None:
                print(f"[yasn] Frontend exited with code {frontend_rc}")
                _stop_process(backend_proc)
                return int(frontend_rc)
            time.sleep(0.2)
    except KeyboardInterrupt:
        print("\n[yasn] Stopping dev/start mode...")
        _stop_process(frontend_proc)
        _stop_process(backend_proc)
        return 130


def load_run_profile(
    mode: str,
    backend: str | None = None,
    host: str | None = None,
    port: int | None = None,
    cwd: Path | None = None,
) -> RunProfile:
    current = (cwd or Path.cwd()).resolve()
    config_path = _find_config(current)
    project_root = config_path.parent if config_path is not None else current

    data: dict[str, Any] = {}
    if config_path is not None:
        data = _read_toml(config_path)

    run_cfg = data.get("run", {})
    if run_cfg is None:
        run_cfg = {}
    if not isinstance(run_cfg, dict):
        raise YasnError("Секция [run] должна быть объектом", path=str(config_path) if config_path else None)

    base_cfg = {k: v for k, v in run_cfg.items() if not isinstance(v, dict)}
    mode_cfg_raw = run_cfg.get(mode, {})
    if mode_cfg_raw is None:
        mode_cfg_raw = {}
    if not isinstance(mode_cfg_raw, dict):
        cfg_path = str(config_path) if config_path else None
        raise YasnError(f"Секция [run.{mode}] должна быть объектом", path=cfg_path)

    backend_path = str(
        backend
        or mode_cfg_raw.get("backend")
        or base_cfg.get("backend")
        or _detect_default_backend(project_root)
        or ""
    ).strip()
    if not backend_path:
        raise YasnError(
            "Не найден backend entrypoint. Укажите [run].backend в yasn.toml или передайте --backend"
        )

    host_value = str(host or mode_cfg_raw.get("host") or base_cfg.get("host") or "127.0.0.1")

    port_raw = port if port is not None else mode_cfg_raw.get("port", base_cfg.get("port", 8000))
    try:
        port_value = int(port_raw)
    except Exception as exc:  # noqa: BLE001
        raise YasnError(f"Некорректный порт: {port_raw}") from exc

    frontend_cmd = mode_cfg_raw.get("frontend")
    if frontend_cmd is None:
        frontend_cmd = mode_cfg_raw.get("frontend_cmd")
    if frontend_cmd is None:
        frontend_cmd = base_cfg.get("frontend")
    if frontend_cmd is None:
        frontend_cmd = base_cfg.get("frontend_cmd")
    if frontend_cmd is not None and not isinstance(frontend_cmd, str):
        raise YasnError(f"run.{mode}.frontend должен быть строкой")

    frontend_cwd = mode_cfg_raw.get("frontend_cwd", mode_cfg_raw.get("frontend_dir", None))
    if frontend_cwd is None:
        frontend_cwd = base_cfg.get("frontend_cwd", base_cfg.get("frontend_dir", None))
    if frontend_cwd is not None and not isinstance(frontend_cwd, str):
        raise YasnError(f"run.{mode}.frontend_cwd должен быть строкой")

    return RunProfile(
        mode=mode,
        backend=backend_path,
        host=host_value,
        port=port_value,
        frontend_cmd=frontend_cmd,
        frontend_cwd=frontend_cwd,
        project_root=project_root,
        config_path=config_path,
    )


def _read_toml(path: Path) -> dict[str, Any]:
    try:
        return tomllib.loads(path.read_text(encoding="utf-8-sig"))
    except Exception as exc:  # noqa: BLE001
        raise YasnError(f"Не удалось прочитать {path.name}: {exc}", path=str(path)) from exc


def _find_config(start: Path) -> Path | None:
    for cur in [start, *start.parents]:
        preferred = cur / "yasn.toml"
        if preferred.exists():
            return preferred
    return None


def _detect_default_backend(root: Path) -> str | None:
    candidates = [
        root / "backend" / "main.яс",
        root / "main.яс",
        root / "app" / "main.яс",
    ]
    for candidate in candidates:
        if candidate.exists():
            return str(candidate.relative_to(root))
    return None


def _resolve_frontend_cwd(profile: RunProfile) -> Path:
    if not profile.frontend_cwd:
        return profile.project_root
    path = Path(profile.frontend_cwd)
    if path.is_absolute():
        return path
    return (profile.project_root / path).resolve()


def _stop_process(proc: subprocess.Popen[Any] | None) -> None:
    if proc is None:
        return
    if proc.poll() is not None:
        return
    proc.terminate()
    try:
        proc.wait(timeout=5)
    except subprocess.TimeoutExpired:
        proc.kill()
        proc.wait(timeout=5)
