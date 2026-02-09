from __future__ import annotations

from dataclasses import dataclass

from . import ast
from .bc import FunctionBC, Instruction, ProgramBC
from .diagnostics import YasnyError
from .optimizer import optimize_program


@dataclass(slots=True)
class CompileResult:
    program: ProgramBC


@dataclass(slots=True)
class _LoopContext:
    break_jumps: list[int]
    continue_jumps: list[int]


class Compiler:
    def __init__(self, path: str | None = None):
        self.path = path

    def compile(self, program: ast.Program) -> CompileResult:
        program = optimize_program(program)
        global_slots = self._collect_global_slots(program)
        functions: dict[str, FunctionBC] = {}

        for stmt in program.statements:
            if isinstance(stmt, ast.FuncDecl):
                fn_compiler = _FunctionCompiler(
                    path=self.path,
                    name=stmt.name,
                    params=[p.name for p in stmt.params],
                    global_slots=global_slots,
                    is_entry=False,
                )
                for body_stmt in stmt.body:
                    fn_compiler.compile_stmt(body_stmt)
                if not fn_compiler.ends_with_terminal():
                    fn_compiler.emit("CONST_NULL")
                    fn_compiler.emit("RET")
                functions[stmt.name] = fn_compiler.finish()

        entry_compiler = _FunctionCompiler(
            path=self.path,
            name="__entry__",
            params=[],
            global_slots=global_slots,
            is_entry=True,
        )
        for stmt in program.statements:
            if isinstance(stmt, ast.FuncDecl):
                continue
            entry_compiler.compile_stmt(stmt)

        if "main" in functions:
            entry_compiler.emit("CALL", "main", 0)
            entry_compiler.emit("POP")
        entry_compiler.emit("HALT")
        entry = entry_compiler.finish()

        return CompileResult(
            program=ProgramBC(
                functions=functions,
                entry=entry,
                global_count=len(global_slots),
            )
        )

    def _collect_global_slots(self, program: ast.Program) -> dict[str, int]:
        slots: dict[str, int] = {}
        for stmt in program.statements:
            if isinstance(stmt, ast.VarDecl) and stmt.name not in slots:
                slots[stmt.name] = len(slots)
        return slots


class _FunctionCompiler:
    def __init__(self, path: str | None, name: str, params: list[str], global_slots: dict[str, int], is_entry: bool):
        self.path = path
        self.name = name
        self.params = params
        self.global_slots = global_slots
        self.is_entry = is_entry
        self.instructions: list[Instruction] = []
        self.next_slot = 0
        self.scopes: list[dict[str, int]] = []
        self.loop_stack: list[_LoopContext] = []

        self.push_scope()
        for param in params:
            self.define_var(param, line=1, col=1)

    def finish(self) -> FunctionBC:
        return FunctionBC(
            name=self.name,
            params=self.params,
            local_count=self.next_slot,
            instructions=self.instructions,
        )

    def ends_with_terminal(self) -> bool:
        return bool(self.instructions) and self.instructions[-1].op in {"RET", "HALT"}

    def emit(self, op: str, *args: object) -> int:
        self.instructions.append(Instruction(op=op, args=list(args)))
        return len(self.instructions) - 1

    def patch(self, idx: int, value: int) -> None:
        self.instructions[idx].args[0] = value

    def current_ip(self) -> int:
        return len(self.instructions)

    def push_scope(self) -> None:
        self.scopes.append({})

    def pop_scope(self) -> None:
        self.scopes.pop()

    def define_var(self, name: str, line: int, col: int) -> int:
        current = self.scopes[-1]
        if name in current:
            raise YasnyError(f"Переменная '{name}' уже объявлена в блоке", line, col, self.path)
        slot = self.allocate_temp()
        current[name] = slot
        return slot

    def allocate_temp(self) -> int:
        slot = self.next_slot
        self.next_slot += 1
        return slot

    def _resolve_local_var(self, name: str) -> int | None:
        for scope in reversed(self.scopes):
            if name in scope:
                return int(scope[name])
        return None

    def resolve_var(self, name: str, line: int, col: int) -> tuple[str, int]:
        local_slot = self._resolve_local_var(name)
        if local_slot is not None:
            return ("local", local_slot)
        if name in self.global_slots:
            return ("global", int(self.global_slots[name]))
        raise YasnyError(f"Неизвестная переменная: {name}", line, col, self.path)

    def compile_stmt(self, stmt: ast.Stmt) -> None:
        if isinstance(stmt, (ast.ImportAll, ast.ImportFrom)):
            raise YasnyError(
                "Операторы подключения должны быть разрешены до этапа генерации байткода",
                stmt.line,
                stmt.col,
                self.path,
            )

        if isinstance(stmt, ast.VarDecl):
            self.compile_expr(stmt.value)
            if self.is_entry and len(self.scopes) == 1 and stmt.name in self.global_slots:
                self.emit("GSTORE", self.global_slots[stmt.name])
            else:
                slot = self.define_var(stmt.name, stmt.line, stmt.col)
                self.emit("STORE", slot)
            return

        if isinstance(stmt, ast.Assign):
            self.compile_expr(stmt.value)
            place, slot = self.resolve_var(stmt.name, stmt.line, stmt.col)
            self.emit("STORE" if place == "local" else "GSTORE", slot)
            return

        if isinstance(stmt, ast.IndexAssign):
            self.compile_expr(stmt.target)
            self.compile_expr(stmt.index)
            self.compile_expr(stmt.value)
            self.emit("INDEX_SET")
            self.emit("POP")
            return

        if isinstance(stmt, ast.ExprStmt):
            self.compile_expr(stmt.expr)
            self.emit("POP")
            return

        if isinstance(stmt, ast.ReturnStmt):
            self.compile_expr(stmt.value)
            self.emit("RET")
            return

        if isinstance(stmt, ast.BreakStmt):
            if not self.loop_stack:
                raise YasnyError("'прервать' допустим только внутри цикла", stmt.line, stmt.col, self.path)
            jmp = self.emit("JMP", -1)
            self.loop_stack[-1].break_jumps.append(jmp)
            return

        if isinstance(stmt, ast.ContinueStmt):
            if not self.loop_stack:
                raise YasnyError("'продолжить' допустим только внутри цикла", stmt.line, stmt.col, self.path)
            jmp = self.emit("JMP", -1)
            self.loop_stack[-1].continue_jumps.append(jmp)
            return

        if isinstance(stmt, ast.IfStmt):
            self.compile_expr(stmt.condition)
            jmp_false = self.emit("JMP_FALSE", -1)

            self.push_scope()
            for inner in stmt.then_body:
                self.compile_stmt(inner)
            self.pop_scope()

            if stmt.else_body is not None:
                jmp_end = self.emit("JMP", -1)
                self.patch(jmp_false, self.current_ip())
                self.push_scope()
                for inner in stmt.else_body:
                    self.compile_stmt(inner)
                self.pop_scope()
                self.patch(jmp_end, self.current_ip())
            else:
                self.patch(jmp_false, self.current_ip())
            return

        if isinstance(stmt, ast.WhileStmt):
            loop_start = self.current_ip()
            self.compile_expr(stmt.condition)
            jmp_end = self.emit("JMP_FALSE", -1)

            self.loop_stack.append(_LoopContext(break_jumps=[], continue_jumps=[]))
            self.push_scope()
            for inner in stmt.body:
                self.compile_stmt(inner)
            self.pop_scope()
            ctx = self.loop_stack.pop()

            for jmp in ctx.continue_jumps:
                self.patch(jmp, loop_start)

            self.emit("JMP", loop_start)
            end_ip = self.current_ip()
            self.patch(jmp_end, end_ip)
            for jmp in ctx.break_jumps:
                self.patch(jmp, end_ip)
            return

        if isinstance(stmt, ast.ForStmt):
            self.push_scope()
            iter_slot = self.allocate_temp()
            idx_slot = self.allocate_temp()
            len_slot = self.allocate_temp()
            loop_var_slot = self.define_var(stmt.var_name, stmt.line, stmt.col)

            self.compile_expr(stmt.iterable)
            self.emit("STORE", iter_slot)
            self.emit("CONST", 0)
            self.emit("STORE", idx_slot)
            self.emit("LOAD", iter_slot)
            self.emit("LEN")
            self.emit("STORE", len_slot)

            loop_start = self.current_ip()
            self.emit("LOAD", idx_slot)
            self.emit("LOAD", len_slot)
            self.emit("LT")
            jmp_end = self.emit("JMP_FALSE", -1)

            self.emit("LOAD", iter_slot)
            self.emit("LOAD", idx_slot)
            self.emit("INDEX_GET")
            self.emit("STORE", loop_var_slot)

            self.loop_stack.append(_LoopContext(break_jumps=[], continue_jumps=[]))
            for inner in stmt.body:
                self.compile_stmt(inner)
            ctx = self.loop_stack.pop()

            increment_start = self.current_ip()
            for jmp in ctx.continue_jumps:
                self.patch(jmp, increment_start)

            self.emit("LOAD", idx_slot)
            self.emit("CONST", 1)
            self.emit("ADD")
            self.emit("STORE", idx_slot)
            self.emit("JMP", loop_start)
            end_ip = self.current_ip()
            self.patch(jmp_end, end_ip)
            for jmp in ctx.break_jumps:
                self.patch(jmp, end_ip)
            self.pop_scope()
            return

        if isinstance(stmt, ast.FuncDecl):
            raise YasnyError("Вложенные функции не поддерживаются", stmt.line, stmt.col, self.path)

        raise YasnyError("Неизвестный узел оператора для компиляции", stmt.line, stmt.col, self.path)

    def compile_expr(self, expr: ast.Expr) -> None:
        if isinstance(expr, ast.Literal):
            self.emit("CONST", expr.value)
            return

        if isinstance(expr, ast.Identifier):
            place, slot = self.resolve_var(expr.name, expr.line, expr.col)
            self.emit("LOAD" if place == "local" else "GLOAD", slot)
            return

        if isinstance(expr, ast.MemberExpr):
            raise YasnyError(
                "Оператор '.' должен быть разрешен на этапе module resolver",
                expr.line,
                expr.col,
                self.path,
            )

        if isinstance(expr, ast.ListLiteral):
            for item in expr.elements:
                self.compile_expr(item)
            self.emit("MAKE_LIST", len(expr.elements))
            return

        if isinstance(expr, ast.DictLiteral):
            for key, value in expr.entries:
                self.compile_expr(key)
                self.compile_expr(value)
            self.emit("MAKE_DICT", len(expr.entries))
            return

        if isinstance(expr, ast.IndexExpr):
            self.compile_expr(expr.target)
            self.compile_expr(expr.index)
            self.emit("INDEX_GET")
            return

        if isinstance(expr, ast.UnaryOp):
            self.compile_expr(expr.operand)
            if expr.op == "не":
                self.emit("NOT")
                return
            if expr.op == "-":
                self.emit("NEG")
                return
            raise YasnyError(f"Неизвестный унарный оператор: {expr.op}", expr.line, expr.col, self.path)

        if isinstance(expr, ast.BinaryOp):
            if expr.op == "и":
                self._compile_short_circuit_and(expr)
                return
            if expr.op == "или":
                self._compile_short_circuit_or(expr)
                return

            self.compile_expr(expr.left)
            self.compile_expr(expr.right)
            op_map = {
                "+": "ADD",
                "-": "SUB",
                "*": "MUL",
                "/": "DIV",
                "%": "MOD",
                "==": "EQ",
                "!=": "NE",
                "<": "LT",
                "<=": "LE",
                ">": "GT",
                ">=": "GE",
            }
            if expr.op not in op_map:
                raise YasnyError(f"Неизвестный бинарный оператор: {expr.op}", expr.line, expr.col, self.path)
            self.emit(op_map[expr.op])
            return

        if isinstance(expr, ast.Call):
            if not isinstance(expr.callee, ast.Identifier):
                raise YasnyError("Вызов возможен только по имени функции", expr.line, expr.col, self.path)
            for arg in expr.args:
                self.compile_expr(arg)
            self.emit("CALL", expr.callee.name, len(expr.args))
            return

        raise YasnyError("Неизвестный узел выражения для компиляции", expr.line, expr.col, self.path)

    def _compile_short_circuit_and(self, expr: ast.BinaryOp) -> None:
        self.compile_expr(expr.left)
        left_false = self.emit("JMP_FALSE", -1)
        self.compile_expr(expr.right)
        right_false = self.emit("JMP_FALSE", -1)
        self.emit("CONST", True)
        jmp_end = self.emit("JMP", -1)
        false_label = self.current_ip()
        self.emit("CONST", False)
        end_label = self.current_ip()
        self.patch(left_false, false_label)
        self.patch(right_false, false_label)
        self.patch(jmp_end, end_label)

    def _compile_short_circuit_or(self, expr: ast.BinaryOp) -> None:
        self.compile_expr(expr.left)
        check_right = self.emit("JMP_FALSE", -1)
        self.emit("CONST", True)
        jmp_end_left_true = self.emit("JMP", -1)

        right_label = self.current_ip()
        self.patch(check_right, right_label)
        self.compile_expr(expr.right)
        right_false = self.emit("JMP_FALSE", -1)
        self.emit("CONST", True)
        jmp_end_right_true = self.emit("JMP", -1)
        false_label = self.current_ip()
        self.emit("CONST", False)
        end_label = self.current_ip()

        self.patch(right_false, false_label)
        self.patch(jmp_end_left_true, end_label)
        self.patch(jmp_end_right_true, end_label)
