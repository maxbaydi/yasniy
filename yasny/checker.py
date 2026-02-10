from __future__ import annotations

from dataclasses import dataclass

from . import ast
from .diagnostics import YasnyError
from .types import (
    ANY,
    BOOL,
    FLOAT,
    INT,
    STRING,
    TASK,
    VOID,
    FunctionSignature,
    Type,
    from_type_node,
    is_assignable,
    is_numeric_like,
    list_of,
    union_of,
    variants_of,
)


@dataclass(slots=True)
class CheckResult:
    function_signatures: dict[str, FunctionSignature]


class TypeChecker:
    def __init__(self, path: str | None = None):
        self.path = path
        self.function_signatures: dict[str, FunctionSignature] = {}
        self.scopes: list[dict[str, Type]] = []
        self.global_symbols: dict[str, Type] = {}
        self.loop_depth = 0
        self._install_builtins()

    def _install_builtins(self) -> None:
        self.function_signatures["печать"] = FunctionSignature(
            name="печать",
            params=[],
            return_type=VOID,
            builtin=True,
            varargs=True,
        )
        self.function_signatures["длина"] = FunctionSignature(
            name="длина",
            params=[],
            return_type=INT,
            builtin=True,
        )
        self.function_signatures["диапазон"] = FunctionSignature(
            name="диапазон",
            params=[INT, INT],
            return_type=list_of(INT),
            builtin=True,
        )
        self.function_signatures["ввод"] = FunctionSignature(
            name="ввод",
            params=[],
            return_type=STRING,
            builtin=True,
        )
        self.function_signatures["пауза"] = FunctionSignature(
            name="пауза",
            params=[INT],
            return_type=VOID,
            builtin=True,
        )
        self.function_signatures["строка"] = FunctionSignature(
            name="строка",
            params=[ANY],
            return_type=STRING,
            builtin=True,
        )
        self.function_signatures["число"] = FunctionSignature(
            name="число",
            params=[ANY],
            return_type=INT,
            builtin=True,
        )
        self.function_signatures["запустить"] = FunctionSignature(
            name="запустить",
            params=[STRING],
            return_type=TASK,
            builtin=True,
            varargs=True,
        )
        self.function_signatures["готово"] = FunctionSignature(
            name="готово",
            params=[TASK],
            return_type=BOOL,
            builtin=True,
        )
        self.function_signatures["ожидать"] = FunctionSignature(
            name="ожидать",
            params=[TASK],
            return_type=ANY,
            builtin=True,
        )
        self.function_signatures["ожидать_все"] = FunctionSignature(
            name="ожидать_все",
            params=[list_of(TASK)],
            return_type=list_of(ANY),
            builtin=True,
        )
        self.function_signatures["отменить"] = FunctionSignature(
            name="отменить",
            params=[TASK],
            return_type=BOOL,
            builtin=True,
        )

    def check(self, program: ast.Program) -> CheckResult:
        function_nodes: list[ast.FuncDecl] = []
        function_positions: dict[str, tuple[int, int]] = {}

        for stmt in program.statements:
            if isinstance(stmt, ast.FuncDecl):
                if stmt.name in self.function_signatures:
                    raise YasnyError(f"Функция '{stmt.name}' уже объявлена", stmt.line, stmt.col, self.path)
                param_types = [from_type_node(p.type_node, self.path) for p in stmt.params]
                return_type = from_type_node(stmt.return_type, self.path)
                self.function_signatures[stmt.name] = FunctionSignature(
                    name=stmt.name,
                    params=param_types,
                    return_type=return_type,
                    builtin=False,
                    is_async=stmt.is_async,
                )
                function_nodes.append(stmt)
                function_positions[stmt.name] = (stmt.line, stmt.col)

        self._push_scope()
        for stmt in program.statements:
            if isinstance(stmt, ast.FuncDecl):
                continue
            if self._check_stmt(stmt, current_return_type=None):
                raise YasnyError("Возврат из функции вне контекста функции", stmt.line, stmt.col, self.path)
        self.global_symbols = self.scopes[-1].copy()
        self._pop_scope()

        for fn in function_nodes:
            sig = self.function_signatures[fn.name]
            self._check_function(fn, sig)

        if "main" in self.function_signatures:
            main_sig = self.function_signatures["main"]
            line, col = function_positions.get("main", (1, 1))
            if main_sig.params:
                raise YasnyError("Функция main должна быть без параметров", line, col, self.path)
            if main_sig.return_type != VOID:
                raise YasnyError("Функция main должна возвращать Пусто", line, col, self.path)
            if main_sig.is_async:
                raise YasnyError("Функция main не может быть асинхронной", line, col, self.path)

        return CheckResult(function_signatures=self.function_signatures.copy())

    def _check_function(self, fn: ast.FuncDecl, sig: FunctionSignature) -> None:
        self._push_scope()
        self.scopes[-1].update(self.global_symbols)
        self._push_scope()
        for param, t in zip(fn.params, sig.params, strict=True):
            self._define_var(param.name, t, param.line, param.col)
        must_return = self._check_block(fn.body, sig.return_type)
        self._pop_scope()
        self._pop_scope()
        if sig.return_type != VOID and not must_return:
            raise YasnyError(
                f"Функция '{fn.name}' должна гарантированно возвращать {sig.return_type}",
                fn.line,
                fn.col,
                self.path,
            )

    def _check_block(self, body: list[ast.Stmt], current_return_type: Type | None) -> bool:
        guaranteed_return = False
        for stmt in body:
            returns = self._check_stmt(stmt, current_return_type)
            if returns:
                guaranteed_return = True
                break
        return guaranteed_return

    def _check_stmt(self, stmt: ast.Stmt, current_return_type: Type | None) -> bool:
        if isinstance(stmt, (ast.ImportAll, ast.ImportFrom)):
            raise YasnyError(
                "Операторы подключения должны быть разрешены до типизации",
                stmt.line,
                stmt.col,
                self.path,
            )

        if isinstance(stmt, ast.VarDecl):
            value_t = self._check_expr(stmt.value)
            if stmt.annotation is not None:
                declared_t = from_type_node(stmt.annotation, self.path)
                if not is_assignable(declared_t, value_t):
                    raise YasnyError(
                        f"Тип переменной '{stmt.name}' ожидается {declared_t}, получен {value_t}",
                        stmt.line,
                        stmt.col,
                        self.path,
                    )
                self._define_var(stmt.name, declared_t, stmt.line, stmt.col)
            else:
                self._define_var(stmt.name, value_t, stmt.line, stmt.col)
            return False

        if isinstance(stmt, ast.Assign):
            var_t = self._resolve_var(stmt.name, stmt.line, stmt.col)
            value_t = self._check_expr(stmt.value)
            if not is_assignable(var_t, value_t):
                raise YasnyError(
                    f"Нельзя присвоить {value_t} в переменную '{stmt.name}' типа {var_t}",
                    stmt.line,
                    stmt.col,
                    self.path,
                )
            return False

        if isinstance(stmt, ast.IndexAssign):
            target_t = self._check_expr(stmt.target)
            index_t = self._check_expr(stmt.index)
            value_t = self._check_expr(stmt.value)
            slot_t = self._index_access_type(target_t, index_t, stmt.line, stmt.col)
            if not is_assignable(slot_t, value_t):
                raise YasnyError(
                    f"Нельзя записать {value_t} в элемент типа {slot_t}",
                    stmt.line,
                    stmt.col,
                    self.path,
                )
            return False

        if isinstance(stmt, ast.IfStmt):
            cond_t = self._check_expr(stmt.condition)
            if cond_t != BOOL:
                raise YasnyError("Условие 'если' должно иметь тип Лог", stmt.condition.line, stmt.condition.col, self.path)

            self._push_scope()
            then_returns = self._check_block(stmt.then_body, current_return_type)
            self._pop_scope()

            else_returns = False
            if stmt.else_body is not None:
                self._push_scope()
                else_returns = self._check_block(stmt.else_body, current_return_type)
                self._pop_scope()
            return then_returns and else_returns and stmt.else_body is not None

        if isinstance(stmt, ast.WhileStmt):
            cond_t = self._check_expr(stmt.condition)
            if cond_t != BOOL:
                raise YasnyError("Условие 'пока' должно иметь тип Лог", stmt.condition.line, stmt.condition.col, self.path)
            self.loop_depth += 1
            self._push_scope()
            self._check_block(stmt.body, current_return_type)
            self._pop_scope()
            self.loop_depth -= 1
            return False

        if isinstance(stmt, ast.ForStmt):
            it_t = self._check_expr(stmt.iterable)
            elem_types: list[Type] = []
            for variant in variants_of(it_t):
                if variant.name != "Список":
                    raise YasnyError(
                        "Цикл 'для' поддерживает только Список[Т]",
                        stmt.iterable.line,
                        stmt.iterable.col,
                        self.path,
                    )
                elem_types.append(variant.args[0])

            self.loop_depth += 1
            self._push_scope()
            self._define_var(stmt.var_name, union_of(*elem_types), stmt.line, stmt.col)
            self._check_block(stmt.body, current_return_type)
            self._pop_scope()
            self.loop_depth -= 1
            return False

        if isinstance(stmt, ast.BreakStmt):
            if self.loop_depth <= 0:
                raise YasnyError("'прервать' допустим только внутри цикла", stmt.line, stmt.col, self.path)
            return False

        if isinstance(stmt, ast.ContinueStmt):
            if self.loop_depth <= 0:
                raise YasnyError("'продолжить' допустим только внутри цикла", stmt.line, stmt.col, self.path)
            return False

        if isinstance(stmt, ast.ReturnStmt):
            if current_return_type is None:
                raise YasnyError("Оператор 'вернуть' разрешён только внутри функции", stmt.line, stmt.col, self.path)
            value_t = self._check_expr(stmt.value)
            if not is_assignable(current_return_type, value_t):
                raise YasnyError(
                    f"Тип возвращаемого значения {value_t}, ожидается {current_return_type}",
                    stmt.line,
                    stmt.col,
                    self.path,
                )
            return True

        if isinstance(stmt, ast.ExprStmt):
            self._check_expr(stmt.expr)
            return False

        if isinstance(stmt, ast.FuncDecl):
            raise YasnyError("Вложенные объявления функций не поддерживаются", stmt.line, stmt.col, self.path)

        raise YasnyError("Неизвестный тип оператора", stmt.line, stmt.col, self.path)

    def _check_expr(self, expr: ast.Expr) -> Type:
        if isinstance(expr, ast.Literal):
            if expr.kind == "int":
                expr.inferred_type = INT
                return INT
            if expr.kind == "float":
                expr.inferred_type = FLOAT
                return FLOAT
            if expr.kind == "string":
                expr.inferred_type = STRING
                return STRING
            if expr.kind == "bool":
                expr.inferred_type = BOOL
                return BOOL
            if expr.kind == "null":
                expr.inferred_type = VOID
                return VOID
            raise YasnyError("Неизвестный литерал", expr.line, expr.col, self.path)

        if isinstance(expr, ast.Identifier):
            t = self._resolve_var(expr.name, expr.line, expr.col)
            expr.inferred_type = t
            return t

        if isinstance(expr, ast.MemberExpr):
            raise YasnyError(
                "Оператор '.' допустим только для пространств модулей и должен быть разрешён до типизации",
                expr.line,
                expr.col,
                self.path,
            )

        if isinstance(expr, ast.ListLiteral):
            if not expr.elements:
                raise YasnyError("Пустой список без аннотации типа недопустим", expr.line, expr.col, self.path)
            element_types = [self._check_expr(e) for e in expr.elements]
            first_t = element_types[0]
            for t, item in zip(element_types[1:], expr.elements[1:], strict=True):
                if t != first_t:
                    raise YasnyError(
                        f"Элементы списка должны быть одного типа: {first_t} и {t}",
                        item.line,
                        item.col,
                        self.path,
                    )
            result = list_of(first_t)
            expr.inferred_type = result
            return result

        if isinstance(expr, ast.DictLiteral):
            if not expr.entries:
                raise YasnyError("Пустой словарь без аннотации типа недопустим", expr.line, expr.col, self.path)
            first_key_t = self._check_expr(expr.entries[0][0])
            first_val_t = self._check_expr(expr.entries[0][1])
            for key, val in expr.entries[1:]:
                key_t = self._check_expr(key)
                val_t = self._check_expr(val)
                if key_t != first_key_t:
                    raise YasnyError(
                        f"Ключи словаря должны быть одного типа: {first_key_t} и {key_t}",
                        key.line,
                        key.col,
                        self.path,
                    )
                if val_t != first_val_t:
                    raise YasnyError(
                        f"Значения словаря должны быть одного типа: {first_val_t} и {val_t}",
                        val.line,
                        val.col,
                        self.path,
                    )
            result = Type("Словарь", (first_key_t, first_val_t))
            expr.inferred_type = result
            return result

        if isinstance(expr, ast.UnaryOp):
            t = self._check_expr(expr.operand)
            if expr.op == "не":
                if t != BOOL:
                    raise YasnyError("Оператор 'не' принимает только Лог", expr.line, expr.col, self.path)
                expr.inferred_type = BOOL
                return BOOL
            if expr.op == "-":
                if not is_numeric_like(t):
                    raise YasnyError("Унарный '-' принимает только Цел/Дроб", expr.line, expr.col, self.path)
                expr.inferred_type = t
                return t
            raise YasnyError(f"Неизвестный унарный оператор: {expr.op}", expr.line, expr.col, self.path)

        if isinstance(expr, ast.AwaitExpr):
            operand_t = self._check_expr(expr.operand)
            if not is_assignable(TASK, operand_t):
                raise YasnyError("Оператор 'ждать' принимает только Задача", expr.line, expr.col, self.path)
            expr.inferred_type = ANY
            return ANY

        if isinstance(expr, ast.BinaryOp):
            left_t = self._check_expr(expr.left)
            right_t = self._check_expr(expr.right)

            if expr.op in {"+", "-", "*", "/", "%"}:
                if expr.op == "+" and left_t == STRING and right_t == STRING:
                    expr.inferred_type = STRING
                    return STRING
                if left_t != right_t:
                    raise YasnyError(
                        f"Операнды '{expr.op}' должны быть одного типа, получены {left_t} и {right_t}",
                        expr.line,
                        expr.col,
                        self.path,
                    )
                if not is_numeric_like(left_t):
                    raise YasnyError(
                        f"Оператор '{expr.op}' поддерживает только Цел/Дроб",
                        expr.line,
                        expr.col,
                        self.path,
                    )
                expr.inferred_type = left_t
                return left_t

            if expr.op in {"==", "!="}:
                if left_t != right_t:
                    raise YasnyError(
                        f"Сравнение '{expr.op}' требует одинаковые типы, получены {left_t} и {right_t}",
                        expr.line,
                        expr.col,
                        self.path,
                    )
                expr.inferred_type = BOOL
                return BOOL

            if expr.op in {"<", "<=", ">", ">="}:
                if left_t != right_t:
                    raise YasnyError(
                        f"Сравнение '{expr.op}' требует одинаковые типы, получены {left_t} и {right_t}",
                        expr.line,
                        expr.col,
                        self.path,
                    )
                for variant in variants_of(left_t):
                    if variant not in (INT, FLOAT, STRING):
                        raise YasnyError(
                            f"Сравнение '{expr.op}' поддерживает Цел/Дроб/Строка",
                            expr.line,
                            expr.col,
                            self.path,
                        )
                expr.inferred_type = BOOL
                return BOOL

            if expr.op in {"и", "или"}:
                if left_t != BOOL or right_t != BOOL:
                    raise YasnyError("Операторы 'и/или' требуют Лог", expr.line, expr.col, self.path)
                expr.inferred_type = BOOL
                return BOOL

            raise YasnyError(f"Неизвестный бинарный оператор: {expr.op}", expr.line, expr.col, self.path)

        if isinstance(expr, ast.IndexExpr):
            target_t = self._check_expr(expr.target)
            index_t = self._check_expr(expr.index)
            result = self._index_access_type(target_t, index_t, expr.line, expr.col)
            expr.inferred_type = result
            return result

        if isinstance(expr, ast.Call):
            if not isinstance(expr.callee, ast.Identifier):
                raise YasnyError("Вызов возможен только по имени функции", expr.line, expr.col, self.path)
            fn_name = expr.callee.name
            arg_types = [self._check_expr(arg) for arg in expr.args]
            if fn_name not in self.function_signatures:
                raise YasnyError(f"Неизвестная функция: {fn_name}", expr.line, expr.col, self.path)
            sig = self.function_signatures[fn_name]

            if sig.builtin:
                result = self._check_builtin_call(expr, fn_name, arg_types)
                expr.inferred_type = result
                return result

            if len(arg_types) != len(sig.params):
                raise YasnyError(
                    f"Функция '{fn_name}' ожидает {len(sig.params)} аргументов, передано {len(arg_types)}",
                    expr.line,
                    expr.col,
                    self.path,
                )
            for idx, (expected, actual) in enumerate(zip(sig.params, arg_types, strict=True), start=1):
                if not is_assignable(expected, actual):
                    raise YasnyError(
                        f"Аргумент {idx} функции '{fn_name}': ожидался {expected}, получен {actual}",
                        expr.line,
                        expr.col,
                        self.path,
                    )
            if sig.is_async:
                expr.inferred_type = TASK
                return TASK
            expr.inferred_type = sig.return_type
            return sig.return_type

        raise YasnyError("Неизвестный тип выражения", expr.line, expr.col, self.path)

    def _index_access_type(self, target_t: Type, index_t: Type, line: int, col: int) -> Type:
        result_types: list[Type] = []
        for variant in variants_of(target_t):
            if variant.name == "Список":
                if not is_assignable(INT, index_t):
                    raise YasnyError("Индекс списка должен иметь тип Цел", line, col, self.path)
                result_types.append(variant.args[0])
                continue
            if variant.name == "Словарь":
                key_t = variant.args[0]
                value_t = variant.args[1]
                if not is_assignable(key_t, index_t):
                    raise YasnyError(
                        f"Тип ключа словаря ожидается {key_t}, получен {index_t}",
                        line,
                        col,
                        self.path,
                    )
                result_types.append(value_t)
                continue
            if variant == STRING:
                if not is_assignable(INT, index_t):
                    raise YasnyError("Индекс строки должен иметь тип Цел", line, col, self.path)
                result_types.append(STRING)
                continue
            raise YasnyError("Индексирование поддерживается только для Список/Словарь/Строка", line, col, self.path)
        return union_of(*result_types)

    def _check_builtin_call(self, expr: ast.Call, name: str, arg_types: list[Type]) -> Type:
        if name == "печать":
            return VOID
        if name == "длина":
            if len(arg_types) != 1:
                raise YasnyError("длина(x) принимает ровно 1 аргумент", expr.line, expr.col, self.path)
            arg_t = arg_types[0]
            for variant in variants_of(arg_t):
                if variant != STRING and variant.name != "Список":
                    raise YasnyError("длина(x) поддерживает только Строка или Список[Т]", expr.line, expr.col, self.path)
            return INT
        if name == "диапазон":
            if len(arg_types) != 2:
                raise YasnyError("диапазон(нач, конец) принимает 2 аргумента", expr.line, expr.col, self.path)
            if not is_assignable(INT, arg_types[0]) or not is_assignable(INT, arg_types[1]):
                raise YasnyError("диапазон(нач, конец) принимает только Цел", expr.line, expr.col, self.path)
            return list_of(INT)
        if name == "ввод":
            if arg_types:
                raise YasnyError("ввод() не принимает аргументы", expr.line, expr.col, self.path)
            return STRING
        if name == "пауза":
            if len(arg_types) != 1:
                raise YasnyError("пауза(мс) принимает ровно 1 аргумент", expr.line, expr.col, self.path)
            if not is_assignable(INT, arg_types[0]):
                raise YasnyError("пауза(мс) принимает только Цел", expr.line, expr.col, self.path)
            return VOID
        if name == "строка":
            if len(arg_types) != 1:
                raise YasnyError("строка(x) принимает ровно 1 аргумент", expr.line, expr.col, self.path)
            return STRING
        if name == "число":
            if len(arg_types) != 1:
                raise YasnyError("число(x) принимает ровно 1 аргумент", expr.line, expr.col, self.path)
            return INT
        if name == "запустить":
            if len(arg_types) < 1:
                raise YasnyError("запустить(имя, ...args) требует минимум 1 аргумент", expr.line, expr.col, self.path)
            if not is_assignable(STRING, arg_types[0]):
                raise YasnyError("Первый аргумент запустить(...) должен быть Строка", expr.line, expr.col, self.path)
            return TASK
        if name == "готово":
            if len(arg_types) != 1:
                raise YasnyError("готово(задача) принимает ровно 1 аргумент", expr.line, expr.col, self.path)
            if not is_assignable(TASK, arg_types[0]):
                raise YasnyError("готово(задача) принимает только Задача", expr.line, expr.col, self.path)
            return BOOL
        if name == "ожидать":
            if len(arg_types) not in (1, 2):
                raise YasnyError("ожидать(задача[, таймаут_мс]) принимает 1 или 2 аргумента", expr.line, expr.col, self.path)
            if not is_assignable(TASK, arg_types[0]):
                raise YasnyError("Первый аргумент ожидать(...) должен быть Задача", expr.line, expr.col, self.path)
            if len(arg_types) == 2 and not is_assignable(INT, arg_types[1]):
                raise YasnyError("Второй аргумент ожидать(...) должен быть Цел", expr.line, expr.col, self.path)
            return ANY
        if name == "ожидать_все":
            if len(arg_types) not in (1, 2):
                raise YasnyError(
                    "ожидать_все(список_задач[, таймаут_мс]) принимает 1 или 2 аргумента",
                    expr.line,
                    expr.col,
                    self.path,
                )
            expected_tasks = list_of(TASK)
            if not is_assignable(expected_tasks, arg_types[0]):
                raise YasnyError(
                    "Первый аргумент ожидать_все(...) должен быть Список[Задача]",
                    expr.line,
                    expr.col,
                    self.path,
                )
            if len(arg_types) == 2 and not is_assignable(INT, arg_types[1]):
                raise YasnyError("Второй аргумент ожидать_все(...) должен быть Цел", expr.line, expr.col, self.path)
            return list_of(ANY)
        if name == "отменить":
            if len(arg_types) != 1:
                raise YasnyError("отменить(задача) принимает ровно 1 аргумент", expr.line, expr.col, self.path)
            if not is_assignable(TASK, arg_types[0]):
                raise YasnyError("отменить(задача) принимает только Задача", expr.line, expr.col, self.path)
            return BOOL
        raise YasnyError(f"Неизвестная встроенная функция: {name}", expr.line, expr.col, self.path)

    def _push_scope(self) -> None:
        self.scopes.append({})

    def _pop_scope(self) -> None:
        self.scopes.pop()

    def _define_var(self, name: str, t: Type, line: int, col: int) -> None:
        scope = self.scopes[-1]
        if name in scope:
            raise YasnyError(f"Переменная '{name}' уже объявлена в этой области", line, col, self.path)
        scope[name] = t

    def _resolve_var(self, name: str, line: int, col: int) -> Type:
        for scope in reversed(self.scopes):
            if name in scope:
                return scope[name]
        raise YasnyError(f"Неизвестная переменная: {name}", line, col, self.path)
