from __future__ import annotations

import copy
import hashlib
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import tomllib

from . import ast
from .diagnostics import YasnyError
from .lexer import tokenize
from .parser import Parser


@dataclass(slots=True)
class ModuleConfig:
    root: str | None
    paths: list[str]


@dataclass(slots=True)
class ResolvedModule:
    path: Path
    program: ast.Program
    exports: dict[str, ast.Stmt]
    tag: str


class ModuleResolver:
    def __init__(self):
        self._resolved: dict[Path, ResolvedModule] = {}
        self._resolving_stack: list[Path] = []
        self._project_root: Path | None = None
        self._config = ModuleConfig(root=None, paths=[])
        self._tags: dict[Path, str] = {}

    def resolve_entry(self, source: str, entry_path: str | None) -> ast.Program:
        if entry_path:
            entry = Path(entry_path).resolve()
        else:
            entry = Path.cwd() / "<stdin>"
        self._init_project_context(entry)
        entry_program = _parse_source_text(source, path=str(entry))
        resolved = self._resolve_module(entry, entry_program, is_entry=True)
        return resolved.program

    def _init_project_context(self, entry: Path) -> None:
        base = entry if entry.is_dir() else entry.parent
        found_project: Path | None = None
        found_config: Path | None = None

        for cur in [base, *base.parents]:
            if (cur / "yasn.toml").exists():
                found_project = cur
                found_config = cur / "yasn.toml"
                break
            if (cur / "yasny.toml").exists():
                found_project = cur
                found_config = cur / "yasny.toml"
                break
            if found_project is None and (cur / "pyproject.toml").exists():
                found_project = cur

        self._project_root = found_project
        if found_config is not None:
            self._config = self._load_config(found_config)

    def _load_config(self, path: Path) -> ModuleConfig:
        raw = path.read_bytes()
        try:
            data = tomllib.loads(raw.decode("utf-8-sig"))
        except Exception as exc:  # noqa: BLE001
            raise YasnyError(f"РќРµ СѓРґР°Р»РѕСЃСЊ РїСЂРѕС‡РёС‚Р°С‚СЊ yasn.toml/yasny.toml: {exc}", path=str(path)) from exc

        modules = data.get("modules", {})
        if not isinstance(modules, dict):
            raise YasnyError("РЎРµРєС†РёСЏ [modules] РІ yasn.toml/yasny.toml РґРѕР»Р¶РЅР° Р±С‹С‚СЊ РѕР±СЉРµРєС‚РѕРј", path=str(path))

        root: str | None = None
        paths: list[str] = []
        if "root" in modules:
            root_val = modules["root"]
            if not isinstance(root_val, str):
                raise YasnyError("modules.root РґРѕР»Р¶РµРЅ Р±С‹С‚СЊ СЃС‚СЂРѕРєРѕР№", path=str(path))
            root = root_val
        if "paths" in modules:
            paths_val = modules["paths"]
            if not isinstance(paths_val, list) or not all(isinstance(x, str) for x in paths_val):
                raise YasnyError("modules.paths РґРѕР»Р¶РµРЅ Р±С‹С‚СЊ СЃРїРёСЃРєРѕРј СЃС‚СЂРѕРє", path=str(path))
            paths = [str(x) for x in paths_val]
        return ModuleConfig(root=root, paths=paths)

    def _resolve_module(self, module_path: Path, program: ast.Program | None, is_entry: bool) -> ResolvedModule:
        module_path = module_path.resolve()
        if module_path in self._resolved:
            return self._resolved[module_path]

        if module_path in self._resolving_stack:
            chain = " -> ".join(str(p) for p in self._resolving_stack + [module_path])
            raise YasnyError(f"РћР±РЅР°СЂСѓР¶РµРЅ С†РёРєР»РёС‡РµСЃРєРёР№ РёРјРїРѕСЂС‚: {chain}", path=str(module_path))

        self._resolving_stack.append(module_path)
        try:
            if program is None:
                source = module_path.read_text(encoding="utf-8")
                program = _parse_source_text(source, path=str(module_path))

            linked_statements = self._link_statements(program.statements, module_path, is_entry=is_entry)
            linked_program = ast.Program(line=program.line, col=program.col, statements=linked_statements)
            exports = self._collect_exports(linked_statements)

            resolved = ResolvedModule(
                path=module_path,
                program=linked_program,
                exports=exports,
                tag=self._module_tag(module_path),
            )
            self._resolved[module_path] = resolved
            return resolved
        finally:
            self._resolving_stack.pop()

    def _collect_exports(self, statements: list[ast.Stmt]) -> dict[str, ast.Stmt]:
        decls: list[ast.Stmt] = [s for s in statements if isinstance(s, (ast.VarDecl, ast.FuncDecl))]
        explicit = any(getattr(s, "exported", False) for s in decls)

        exports: dict[str, ast.Stmt] = {}
        for stmt in decls:
            name = _decl_name(stmt)
            if name is None:
                continue
            if name == "main":
                continue
            if name.startswith("__РјРѕРґ_"):
                continue
            if explicit and not getattr(stmt, "exported", False):
                continue
            exports[name] = copy.deepcopy(stmt)
        return exports

    def _link_statements(self, statements: list[ast.Stmt], module_path: Path, is_entry: bool) -> list[ast.Stmt]:
        linked: list[ast.Stmt] = []
        top_decl_names: set[str] = set()
        import_name_map: dict[str, str] = {}
        namespace_map: dict[str, dict[str, str]] = {}
        non_import_seen = False

        for stmt in statements:
            if isinstance(stmt, (ast.ImportAll, ast.ImportFrom)):
                if non_import_seen:
                    raise YasnyError(
                        "РћРїРµСЂР°С‚РѕСЂС‹ 'РїРѕРґРєР»СЋС‡РёС‚СЊ/РёР· ... РїРѕРґРєР»СЋС‡РёС‚СЊ' РґРѕР»Р¶РЅС‹ РёРґС‚Рё РґРѕ РѕСЃС‚Р°Р»СЊРЅС‹С… РѕР±СЉСЏРІР»РµРЅРёР№",
                        stmt.line,
                        stmt.col,
                        str(module_path),
                    )
                imported = self._resolve_import(stmt, module_path, import_name_map, namespace_map, top_decl_names)
                linked.extend(imported)
                continue

            non_import_seen = True

            if not is_entry and not isinstance(stmt, (ast.VarDecl, ast.FuncDecl)):
                raise YasnyError(
                    "Р’ РїРѕРґРєР»СЋС‡Р°РµРјРѕРј РјРѕРґСѓР»Рµ СЂР°Р·СЂРµС€РµРЅС‹ С‚РѕР»СЊРєРѕ РѕР±СЉСЏРІР»РµРЅРёСЏ Рё РІР»РѕР¶РµРЅРЅС‹Рµ Р±Р»РѕРєРё РІРЅСѓС‚СЂРё С„СѓРЅРєС†РёР№",
                    stmt.line,
                    stmt.col,
                    str(module_path),
                )

            decl_name = _decl_name(stmt)
            if decl_name is not None:
                if decl_name in import_name_map:
                    raise YasnyError(
                        f"Конфликт имён: '{decl_name}' уже импортировано в эту область",
                        stmt.line,
                        stmt.col,
                        str(module_path),
                    )
                if decl_name in namespace_map:
                    raise YasnyError(
                        f"Конфликт имён: '{decl_name}' уже занято как пространство модуля",
                        stmt.line,
                        stmt.col,
                        str(module_path),
                    )

            rewritten = _AliasRewriter(import_name_map, namespace_map).rewrite_stmt(stmt)
            self._append_decl_with_conflict_check(
                linked=linked,
                names_in_scope=top_decl_names,
                stmt=rewritten,
                source_line=stmt.line,
                source_col=stmt.col,
                source_path=module_path,
            )

        return linked

    def _resolve_import(
        self,
        stmt: ast.Stmt,
        current_module: Path,
        import_name_map: dict[str, str],
        namespace_map: dict[str, dict[str, str]],
        top_decl_names: set[str],
    ) -> list[ast.Stmt]:
        if isinstance(stmt, ast.ImportAll):
            return self._resolve_import_all(stmt, current_module, import_name_map, namespace_map, top_decl_names)
        if isinstance(stmt, ast.ImportFrom):
            return self._resolve_import_from(stmt, current_module, import_name_map, top_decl_names)
        return []

    def _resolve_import_all(
        self,
        stmt: ast.ImportAll,
        current_module: Path,
        import_name_map: dict[str, str],
        namespace_map: dict[str, dict[str, str]],
        top_decl_names: set[str],
    ) -> list[ast.Stmt]:
        target = self._resolve_module_path(stmt.module_path, current_module, stmt.line, stmt.col)
        resolved = self._resolve_module(target, program=None, is_entry=False)
        names = list(resolved.exports.keys())
        materialized, expose_map = self._materialize_imported_decls(resolved, names)
        materialized = self._only_new_imported(materialized, top_decl_names)
        self._append_imported(materialized, top_decl_names, stmt, current_module)

        if stmt.alias is not None:
            alias = stmt.alias
            if alias in namespace_map or alias in import_name_map or alias in top_decl_names:
                raise YasnyError(
                    f"РљРѕРЅС„Р»РёРєС‚ РёРјРµРЅРё РїСЂРѕСЃС‚СЂР°РЅСЃС‚РІР° РјРѕРґСѓР»РµР№: '{alias}'",
                    stmt.line,
                    stmt.col,
                    str(current_module),
                )
            namespace_map[alias] = expose_map
            return materialized

        for exported_name, unique_name in expose_map.items():
            if exported_name in import_name_map or exported_name in top_decl_names:
                raise YasnyError(
                    f"РљРѕРЅС„Р»РёРєС‚ РёРјС‘РЅ РїСЂРё РїРѕРґРєР»СЋС‡РµРЅРёРё: '{exported_name}' СѓР¶Рµ РѕР±СЉСЏРІР»РµРЅРѕ",
                    stmt.line,
                    stmt.col,
                    str(current_module),
                )
            import_name_map[exported_name] = unique_name
        return materialized

    def _resolve_import_from(
        self,
        stmt: ast.ImportFrom,
        current_module: Path,
        import_name_map: dict[str, str],
        top_decl_names: set[str],
    ) -> list[ast.Stmt]:
        target = self._resolve_module_path(stmt.module_path, current_module, stmt.line, stmt.col)
        resolved = self._resolve_module(target, program=None, is_entry=False)

        requested_names: list[str] = []
        for item in stmt.items:
            if item.name not in resolved.exports:
                raise YasnyError(
                    f"РЎРёРјРІРѕР» '{item.name}' РЅРµ РЅР°Р№РґРµРЅ РІ РјРѕРґСѓР»Рµ '{target}'",
                    stmt.line,
                    stmt.col,
                    str(current_module),
                )
            if item.name not in requested_names:
                requested_names.append(item.name)

        include_set = self._expand_with_dependencies(resolved, requested_names)
        materialized, expose_map = self._materialize_imported_decls(resolved, list(include_set))
        materialized = self._only_new_imported(materialized, top_decl_names)
        self._append_imported(materialized, top_decl_names, stmt, current_module)

        seen_local_names: set[str] = set()
        for item in stmt.items:
            local_name = item.alias or item.name
            if local_name in seen_local_names:
                continue
            seen_local_names.add(local_name)
            if local_name in import_name_map or local_name in top_decl_names:
                raise YasnyError(
                    f"РљРѕРЅС„Р»РёРєС‚ РёРјРµРЅРё РїСЂРё РїРѕРґРєР»СЋС‡РµРЅРёРё: '{local_name}' СѓР¶Рµ РѕР±СЉСЏРІР»РµРЅРѕ",
                    item.line,
                    item.col,
                    str(current_module),
                )
            import_name_map[local_name] = expose_map[item.name]
        return materialized

    def _expand_with_dependencies(self, resolved: ResolvedModule, roots: list[str]) -> set[str]:
        include_set: set[str] = set()
        queue = list(roots)
        export_names = set(resolved.exports.keys())

        while queue:
            cur = queue.pop()
            if cur in include_set:
                continue
            include_set.add(cur)
            decl = resolved.exports[cur]
            deps = _direct_dependencies(decl, export_names)
            for dep in deps:
                if dep not in include_set:
                    queue.append(dep)
        return include_set

    def _materialize_imported_decls(
        self,
        resolved: ResolvedModule,
        names: list[str],
    ) -> tuple[list[ast.Stmt], dict[str, str]]:
        selected = {name for name in names if name in resolved.exports}
        rename_map: dict[str, str] = {}
        for name in selected:
            rename_map[name] = self._unique_symbol_name(resolved, name)

        materialized: list[ast.Stmt] = []
        renamer = _RenameSymbols(rename_map)
        for stmt in resolved.program.statements:
            name = _decl_name(stmt)
            if name is None or name not in selected:
                continue
            cloned = copy.deepcopy(stmt)
            renamed = renamer.rewrite_stmt(cloned)
            if isinstance(renamed, ast.VarDecl):
                renamed.exported = False
            if isinstance(renamed, ast.FuncDecl):
                renamed.exported = False
            materialized.append(renamed)

        return materialized, rename_map

    def _append_imported(
        self,
        imported: list[ast.Stmt],
        top_decl_names: set[str],
        stmt: ast.Stmt,
        current_module: Path,
    ) -> None:
        for imported_stmt in imported:
            name = _decl_name(imported_stmt)
            if name and name in top_decl_names:
                continue
            if name:
                top_decl_names.add(name)

    def _only_new_imported(self, imported: list[ast.Stmt], top_decl_names: set[str]) -> list[ast.Stmt]:
        result: list[ast.Stmt] = []
        for imported_stmt in imported:
            name = _decl_name(imported_stmt)
            if name and name in top_decl_names:
                continue
            result.append(imported_stmt)
        return result

    def _append_decl_with_conflict_check(
        self,
        linked: list[ast.Stmt],
        names_in_scope: set[str],
        stmt: ast.Stmt,
        source_line: int,
        source_col: int,
        source_path: Path,
    ) -> None:
        name = _decl_name(stmt)
        if name is not None:
            if name in names_in_scope:
                raise YasnyError(
                    f"РљРѕРЅС„Р»РёРєС‚ РёРјС‘РЅ: '{name}' СѓР¶Рµ РѕР±СЉСЏРІР»РµРЅРѕ",
                    source_line,
                    source_col,
                    str(source_path),
                )
            names_in_scope.add(name)
        linked.append(stmt)

    def _resolve_module_path(self, raw_path: str, current_module: Path, line: int, col: int) -> Path:
        path = Path(raw_path)
        if not path.suffix:
            path = path.with_suffix(".СЏСЃ")

        candidates: list[Path] = []
        if path.is_absolute():
            candidates.append(path.resolve())
        else:
            candidates.append((current_module.parent / path).resolve())
            if self._project_root is not None:
                if self._config.root:
                    candidates.append((self._project_root / self._config.root / path).resolve())
                for extra in self._config.paths:
                    candidates.append((self._project_root / extra / path).resolve())

        dedup: list[Path] = []
        seen: set[Path] = set()
        for candidate in candidates:
            if candidate in seen:
                continue
            seen.add(candidate)
            dedup.append(candidate)

        for candidate in dedup:
            if candidate.exists():
                return candidate

        text = "; ".join(str(x) for x in dedup)
        raise YasnyError(
            f"РњРѕРґСѓР»СЊ РЅРµ РЅР°Р№РґРµРЅ: '{raw_path}'. РџСЂРѕРІРµСЂРµРЅС‹ РїСѓС‚Рё: {text}",
            line,
            col,
            str(current_module),
        )

    def _module_tag(self, path: Path) -> str:
        if path in self._tags:
            return self._tags[path]
        digest = hashlib.sha1(str(path).encode("utf-8")).hexdigest()[:8]
        tag = f"РјРѕРґ_{digest}"
        self._tags[path] = tag
        return tag

    def _unique_symbol_name(self, module: ResolvedModule, original: str) -> str:
        return f"__{module.tag}_{original}"


def _decl_name(stmt: ast.Stmt) -> str | None:
    if isinstance(stmt, ast.VarDecl):
        return stmt.name
    if isinstance(stmt, ast.FuncDecl):
        return stmt.name
    return None


def resolve_modules(source: str, path: str | None) -> ast.Program:
    resolver = ModuleResolver()
    return resolver.resolve_entry(source, path)


def _parse_source_text(source: str, path: str | None) -> ast.Program:
    tokens = tokenize(source, path=path)
    parser = Parser(tokens=tokens, path=path)
    return parser.parse()


def _direct_dependencies(stmt: ast.Stmt, export_names: set[str]) -> set[str]:
    collector = _DependencyCollector(export_names)
    if isinstance(stmt, ast.VarDecl):
        collector.collect_expr(stmt.value)
        collector.deps.discard(stmt.name)
    elif isinstance(stmt, ast.FuncDecl):
        collector.collect_function(stmt)
        collector.deps.discard(stmt.name)
    return collector.deps


class _DependencyCollector:
    BUILTIN_NAMES = {"РїРµС‡Р°С‚СЊ", "РґР»РёРЅР°", "РґРёР°РїР°Р·РѕРЅ", "РІРІРѕРґ"}

    def __init__(self, export_names: set[str]):
        self.export_names = export_names
        self.deps: set[str] = set()
        self.scopes: list[set[str]] = []

    def collect_function(self, fn: ast.FuncDecl) -> None:
        self.push_scope()
        for param in fn.params:
            self.define(param.name)
        for stmt in fn.body:
            self.collect_stmt(stmt)
        self.pop_scope()

    def collect_stmt(self, stmt: ast.Stmt) -> None:
        if isinstance(stmt, ast.VarDecl):
            self.collect_expr(stmt.value)
            self.define(stmt.name)
            return
        if isinstance(stmt, ast.Assign):
            self._consider_name(stmt.name)
            self.collect_expr(stmt.value)
            return
        if isinstance(stmt, ast.IndexAssign):
            self.collect_expr(stmt.target)
            self.collect_expr(stmt.index)
            self.collect_expr(stmt.value)
            return
        if isinstance(stmt, ast.IfStmt):
            self.collect_expr(stmt.condition)
            self.push_scope()
            for s in stmt.then_body:
                self.collect_stmt(s)
            self.pop_scope()
            if stmt.else_body is not None:
                self.push_scope()
                for s in stmt.else_body:
                    self.collect_stmt(s)
                self.pop_scope()
            return
        if isinstance(stmt, ast.WhileStmt):
            self.collect_expr(stmt.condition)
            self.push_scope()
            for s in stmt.body:
                self.collect_stmt(s)
            self.pop_scope()
            return
        if isinstance(stmt, ast.ForStmt):
            self.collect_expr(stmt.iterable)
            self.push_scope()
            self.define(stmt.var_name)
            for s in stmt.body:
                self.collect_stmt(s)
            self.pop_scope()
            return
        if isinstance(stmt, ast.ReturnStmt):
            self.collect_expr(stmt.value)
            return
        if isinstance(stmt, ast.ExprStmt):
            self.collect_expr(stmt.expr)
            return

    def collect_expr(self, expr: ast.Expr) -> None:
        if isinstance(expr, ast.Identifier):
            self._consider_name(expr.name)
            return
        if isinstance(expr, ast.Literal):
            return
        if isinstance(expr, ast.ListLiteral):
            for item in expr.elements:
                self.collect_expr(item)
            return
        if isinstance(expr, ast.DictLiteral):
            for key, value in expr.entries:
                self.collect_expr(key)
                self.collect_expr(value)
            return
        if isinstance(expr, ast.UnaryOp):
            self.collect_expr(expr.operand)
            return
        if isinstance(expr, ast.BinaryOp):
            self.collect_expr(expr.left)
            self.collect_expr(expr.right)
            return
        if isinstance(expr, ast.IndexExpr):
            self.collect_expr(expr.target)
            self.collect_expr(expr.index)
            return
        if isinstance(expr, ast.MemberExpr):
            self.collect_expr(expr.target)
            return
        if isinstance(expr, ast.Call):
            self.collect_expr(expr.callee)
            for arg in expr.args:
                self.collect_expr(arg)
            return

    def _consider_name(self, name: str) -> None:
        if name in self.BUILTIN_NAMES:
            return
        if self._is_local(name):
            return
        if name in self.export_names:
            self.deps.add(name)

    def _is_local(self, name: str) -> bool:
        for scope in reversed(self.scopes):
            if name in scope:
                return True
        return False

    def define(self, name: str) -> None:
        if self.scopes:
            self.scopes[-1].add(name)

    def push_scope(self) -> None:
        self.scopes.append(set())

    def pop_scope(self) -> None:
        self.scopes.pop()


class _AliasRewriter:
    def __init__(self, name_map: dict[str, str], namespace_map: dict[str, dict[str, str]]):
        self.name_map = name_map
        self.namespace_map = namespace_map
        self.scopes: list[set[str]] = []

    def rewrite_stmt(self, stmt: ast.Stmt) -> ast.Stmt:
        if isinstance(stmt, ast.VarDecl):
            value = self.rewrite_expr(stmt.value)
            rewritten = copy.deepcopy(stmt)
            rewritten.value = value
            self.define(rewritten.name)
            return rewritten

        if isinstance(stmt, ast.Assign):
            rewritten = copy.deepcopy(stmt)
            if not self._is_local(stmt.name) and stmt.name in self.name_map:
                rewritten.name = self.name_map[stmt.name]
            rewritten.value = self.rewrite_expr(stmt.value)
            return rewritten

        if isinstance(stmt, ast.IndexAssign):
            rewritten = copy.deepcopy(stmt)
            rewritten.target = self.rewrite_expr(stmt.target)
            rewritten.index = self.rewrite_expr(stmt.index)
            rewritten.value = self.rewrite_expr(stmt.value)
            return rewritten

        if isinstance(stmt, ast.FuncDecl):
            rewritten = copy.deepcopy(stmt)
            self.push_scope()
            for p in rewritten.params:
                self.define(p.name)
            rewritten.body = [self.rewrite_stmt(x) for x in rewritten.body]
            self.pop_scope()
            return rewritten

        if isinstance(stmt, ast.IfStmt):
            rewritten = copy.deepcopy(stmt)
            rewritten.condition = self.rewrite_expr(stmt.condition)
            self.push_scope()
            rewritten.then_body = [self.rewrite_stmt(x) for x in stmt.then_body]
            self.pop_scope()
            if stmt.else_body is not None:
                self.push_scope()
                rewritten.else_body = [self.rewrite_stmt(x) for x in stmt.else_body]
                self.pop_scope()
            return rewritten

        if isinstance(stmt, ast.WhileStmt):
            rewritten = copy.deepcopy(stmt)
            rewritten.condition = self.rewrite_expr(stmt.condition)
            self.push_scope()
            rewritten.body = [self.rewrite_stmt(x) for x in stmt.body]
            self.pop_scope()
            return rewritten

        if isinstance(stmt, ast.ForStmt):
            rewritten = copy.deepcopy(stmt)
            rewritten.iterable = self.rewrite_expr(stmt.iterable)
            self.push_scope()
            self.define(stmt.var_name)
            rewritten.body = [self.rewrite_stmt(x) for x in stmt.body]
            self.pop_scope()
            return rewritten

        if isinstance(stmt, ast.ReturnStmt):
            rewritten = copy.deepcopy(stmt)
            rewritten.value = self.rewrite_expr(stmt.value)
            return rewritten

        if isinstance(stmt, ast.ExprStmt):
            rewritten = copy.deepcopy(stmt)
            rewritten.expr = self.rewrite_expr(stmt.expr)
            return rewritten

        return copy.deepcopy(stmt)

    def rewrite_expr(self, expr: ast.Expr) -> ast.Expr:
        if isinstance(expr, ast.Identifier):
            if not self._is_local(expr.name) and expr.name in self.name_map:
                return ast.Identifier(line=expr.line, col=expr.col, name=self.name_map[expr.name])
            return copy.deepcopy(expr)

        if isinstance(expr, ast.MemberExpr):
            target = self.rewrite_expr(expr.target)
            if isinstance(target, ast.Identifier) and target.name in self.namespace_map:
                ns = self.namespace_map[target.name]
                if expr.member not in ns:
                    raise YasnyError(
                        f"РњРѕРґСѓР»СЊ '{target.name}' РЅРµ СЃРѕРґРµСЂР¶РёС‚ СЃРёРјРІРѕР» '{expr.member}'",
                        expr.line,
                        expr.col,
                    )
                return ast.Identifier(line=expr.line, col=expr.col, name=ns[expr.member])
            rewritten = copy.deepcopy(expr)
            rewritten.target = target
            return rewritten

        if isinstance(expr, ast.Literal):
            return copy.deepcopy(expr)

        if isinstance(expr, ast.ListLiteral):
            rewritten = copy.deepcopy(expr)
            rewritten.elements = [self.rewrite_expr(x) for x in expr.elements]
            return rewritten

        if isinstance(expr, ast.DictLiteral):
            rewritten = copy.deepcopy(expr)
            rewritten.entries = [(self.rewrite_expr(k), self.rewrite_expr(v)) for k, v in expr.entries]
            return rewritten

        if isinstance(expr, ast.UnaryOp):
            rewritten = copy.deepcopy(expr)
            rewritten.operand = self.rewrite_expr(expr.operand)
            return rewritten

        if isinstance(expr, ast.BinaryOp):
            rewritten = copy.deepcopy(expr)
            rewritten.left = self.rewrite_expr(expr.left)
            rewritten.right = self.rewrite_expr(expr.right)
            return rewritten

        if isinstance(expr, ast.IndexExpr):
            rewritten = copy.deepcopy(expr)
            rewritten.target = self.rewrite_expr(expr.target)
            rewritten.index = self.rewrite_expr(expr.index)
            return rewritten

        if isinstance(expr, ast.Call):
            rewritten = copy.deepcopy(expr)
            rewritten.callee = self.rewrite_expr(expr.callee)
            rewritten.args = [self.rewrite_expr(a) for a in expr.args]
            return rewritten

        return copy.deepcopy(expr)

    def push_scope(self) -> None:
        self.scopes.append(set())

    def pop_scope(self) -> None:
        self.scopes.pop()

    def define(self, name: str) -> None:
        if self.scopes:
            self.scopes[-1].add(name)

    def _is_local(self, name: str) -> bool:
        for scope in reversed(self.scopes):
            if name in scope:
                return True
        return False


class _RenameSymbols:
    def __init__(self, rename_map: dict[str, str]):
        self.rename_map = rename_map
        self.scopes: list[set[str]] = []

    def rewrite_stmt(self, stmt: ast.Stmt) -> ast.Stmt:
        if isinstance(stmt, ast.VarDecl):
            rewritten = copy.deepcopy(stmt)
            if stmt.name in self.rename_map:
                rewritten.name = self.rename_map[stmt.name]
            rewritten.value = self.rewrite_expr(stmt.value)
            self.define(rewritten.name)
            return rewritten

        if isinstance(stmt, ast.Assign):
            rewritten = copy.deepcopy(stmt)
            if not self._is_local(stmt.name) and stmt.name in self.rename_map:
                rewritten.name = self.rename_map[stmt.name]
            rewritten.value = self.rewrite_expr(stmt.value)
            return rewritten

        if isinstance(stmt, ast.IndexAssign):
            rewritten = copy.deepcopy(stmt)
            rewritten.target = self.rewrite_expr(stmt.target)
            rewritten.index = self.rewrite_expr(stmt.index)
            rewritten.value = self.rewrite_expr(stmt.value)
            return rewritten

        if isinstance(stmt, ast.FuncDecl):
            rewritten = copy.deepcopy(stmt)
            if stmt.name in self.rename_map:
                rewritten.name = self.rename_map[stmt.name]
            self.push_scope()
            for p in rewritten.params:
                self.define(p.name)
            rewritten.body = [self.rewrite_stmt(x) for x in stmt.body]
            self.pop_scope()
            return rewritten

        if isinstance(stmt, ast.IfStmt):
            rewritten = copy.deepcopy(stmt)
            rewritten.condition = self.rewrite_expr(stmt.condition)
            self.push_scope()
            rewritten.then_body = [self.rewrite_stmt(x) for x in stmt.then_body]
            self.pop_scope()
            if stmt.else_body is not None:
                self.push_scope()
                rewritten.else_body = [self.rewrite_stmt(x) for x in stmt.else_body]
                self.pop_scope()
            return rewritten

        if isinstance(stmt, ast.WhileStmt):
            rewritten = copy.deepcopy(stmt)
            rewritten.condition = self.rewrite_expr(stmt.condition)
            self.push_scope()
            rewritten.body = [self.rewrite_stmt(x) for x in stmt.body]
            self.pop_scope()
            return rewritten

        if isinstance(stmt, ast.ForStmt):
            rewritten = copy.deepcopy(stmt)
            rewritten.iterable = self.rewrite_expr(stmt.iterable)
            self.push_scope()
            self.define(stmt.var_name)
            rewritten.body = [self.rewrite_stmt(x) for x in stmt.body]
            self.pop_scope()
            return rewritten

        if isinstance(stmt, ast.ReturnStmt):
            rewritten = copy.deepcopy(stmt)
            rewritten.value = self.rewrite_expr(stmt.value)
            return rewritten

        if isinstance(stmt, ast.ExprStmt):
            rewritten = copy.deepcopy(stmt)
            rewritten.expr = self.rewrite_expr(stmt.expr)
            return rewritten

        return copy.deepcopy(stmt)

    def rewrite_expr(self, expr: ast.Expr) -> ast.Expr:
        if isinstance(expr, ast.Identifier):
            if not self._is_local(expr.name) and expr.name in self.rename_map:
                return ast.Identifier(line=expr.line, col=expr.col, name=self.rename_map[expr.name])
            return copy.deepcopy(expr)

        if isinstance(expr, ast.MemberExpr):
            rewritten = copy.deepcopy(expr)
            rewritten.target = self.rewrite_expr(expr.target)
            return rewritten

        if isinstance(expr, ast.Literal):
            return copy.deepcopy(expr)

        if isinstance(expr, ast.ListLiteral):
            rewritten = copy.deepcopy(expr)
            rewritten.elements = [self.rewrite_expr(x) for x in expr.elements]
            return rewritten

        if isinstance(expr, ast.DictLiteral):
            rewritten = copy.deepcopy(expr)
            rewritten.entries = [(self.rewrite_expr(k), self.rewrite_expr(v)) for k, v in expr.entries]
            return rewritten

        if isinstance(expr, ast.UnaryOp):
            rewritten = copy.deepcopy(expr)
            rewritten.operand = self.rewrite_expr(expr.operand)
            return rewritten

        if isinstance(expr, ast.BinaryOp):
            rewritten = copy.deepcopy(expr)
            rewritten.left = self.rewrite_expr(expr.left)
            rewritten.right = self.rewrite_expr(expr.right)
            return rewritten

        if isinstance(expr, ast.IndexExpr):
            rewritten = copy.deepcopy(expr)
            rewritten.target = self.rewrite_expr(expr.target)
            rewritten.index = self.rewrite_expr(expr.index)
            return rewritten

        if isinstance(expr, ast.Call):
            rewritten = copy.deepcopy(expr)
            rewritten.callee = self.rewrite_expr(expr.callee)
            rewritten.args = [self.rewrite_expr(a) for a in expr.args]
            return rewritten

        return copy.deepcopy(expr)

    def push_scope(self) -> None:
        self.scopes.append(set())

    def pop_scope(self) -> None:
        self.scopes.pop()

    def define(self, name: str) -> None:
        if self.scopes:
            self.scopes[-1].add(name)

    def _is_local(self, name: str) -> bool:
        for scope in reversed(self.scopes):
            if name in scope:
                return True
        return False



