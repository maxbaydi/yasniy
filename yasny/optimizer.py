from __future__ import annotations

import copy
from dataclasses import dataclass

from . import ast


@dataclass(slots=True)
class OptimizedStmt:
    statements: list[ast.Stmt]
    terminal: bool


def optimize_program(program: ast.Program) -> ast.Program:
    opt = _Optimizer()
    statements = opt.optimize_block(program.statements)
    shaken = _tree_shake(statements)
    return ast.Program(line=program.line, col=program.col, statements=shaken)


class _Optimizer:
    def optimize_block(self, statements: list[ast.Stmt]) -> list[ast.Stmt]:
        out: list[ast.Stmt] = []
        for stmt in statements:
            optimized = self.optimize_stmt(stmt)
            out.extend(optimized.statements)
            if optimized.terminal:
                break
        return out

    def optimize_stmt(self, stmt: ast.Stmt) -> OptimizedStmt:
        if isinstance(stmt, ast.VarDecl):
            rewritten = copy.deepcopy(stmt)
            rewritten.value = self.optimize_expr(stmt.value)
            return OptimizedStmt([rewritten], terminal=False)

        if isinstance(stmt, ast.Assign):
            rewritten = copy.deepcopy(stmt)
            rewritten.value = self.optimize_expr(stmt.value)
            return OptimizedStmt([rewritten], terminal=False)

        if isinstance(stmt, ast.IndexAssign):
            rewritten = copy.deepcopy(stmt)
            rewritten.target = self.optimize_expr(stmt.target)
            rewritten.index = self.optimize_expr(stmt.index)
            rewritten.value = self.optimize_expr(stmt.value)
            return OptimizedStmt([rewritten], terminal=False)

        if isinstance(stmt, ast.ExprStmt):
            rewritten = copy.deepcopy(stmt)
            rewritten.expr = self.optimize_expr(stmt.expr)
            if _is_pure_expression(rewritten.expr):
                return OptimizedStmt([], terminal=False)
            return OptimizedStmt([rewritten], terminal=False)

        if isinstance(stmt, ast.ReturnStmt):
            rewritten = copy.deepcopy(stmt)
            rewritten.value = self.optimize_expr(stmt.value)
            return OptimizedStmt([rewritten], terminal=True)

        if isinstance(stmt, ast.BreakStmt):
            return OptimizedStmt([copy.deepcopy(stmt)], terminal=True)

        if isinstance(stmt, ast.ContinueStmt):
            return OptimizedStmt([copy.deepcopy(stmt)], terminal=True)

        if isinstance(stmt, ast.IfStmt):
            condition = self.optimize_expr(stmt.condition)
            if isinstance(condition, ast.Literal) and condition.kind == "bool":
                if bool(condition.value):
                    then_block = self.optimize_block(stmt.then_body)
                    return OptimizedStmt(then_block, terminal=_block_terminal(then_block))
                else:
                    else_block = self.optimize_block(stmt.else_body or [])
                    return OptimizedStmt(else_block, terminal=_block_terminal(else_block))

            rewritten = copy.deepcopy(stmt)
            rewritten.condition = condition
            rewritten.then_body = self.optimize_block(stmt.then_body)
            rewritten.else_body = self.optimize_block(stmt.else_body) if stmt.else_body is not None else None
            terminal = (
                _block_terminal(rewritten.then_body)
                and rewritten.else_body is not None
                and _block_terminal(rewritten.else_body)
            )
            return OptimizedStmt([rewritten], terminal=terminal)

        if isinstance(stmt, ast.WhileStmt):
            condition = self.optimize_expr(stmt.condition)
            if isinstance(condition, ast.Literal) and condition.kind == "bool" and condition.value is False:
                return OptimizedStmt([], terminal=False)

            rewritten = copy.deepcopy(stmt)
            rewritten.condition = condition
            rewritten.body = self.optimize_block(stmt.body)
            return OptimizedStmt([rewritten], terminal=False)

        if isinstance(stmt, ast.ForStmt):
            rewritten = copy.deepcopy(stmt)
            rewritten.iterable = self.optimize_expr(stmt.iterable)
            rewritten.body = self.optimize_block(stmt.body)
            return OptimizedStmt([rewritten], terminal=False)

        if isinstance(stmt, ast.FuncDecl):
            rewritten = copy.deepcopy(stmt)
            rewritten.body = self.optimize_block(stmt.body)
            return OptimizedStmt([rewritten], terminal=False)

        if isinstance(stmt, (ast.ImportAll, ast.ImportFrom)):
            return OptimizedStmt([copy.deepcopy(stmt)], terminal=False)

        return OptimizedStmt([copy.deepcopy(stmt)], terminal=False)

    def optimize_expr(self, expr: ast.Expr) -> ast.Expr:
        if isinstance(expr, ast.Literal):
            return copy.deepcopy(expr)
        if isinstance(expr, ast.Identifier):
            return copy.deepcopy(expr)
        if isinstance(expr, ast.MemberExpr):
            rewritten = copy.deepcopy(expr)
            rewritten.target = self.optimize_expr(expr.target)
            return rewritten
        if isinstance(expr, ast.IndexExpr):
            rewritten = copy.deepcopy(expr)
            rewritten.target = self.optimize_expr(expr.target)
            rewritten.index = self.optimize_expr(expr.index)
            return rewritten
        if isinstance(expr, ast.ListLiteral):
            rewritten = copy.deepcopy(expr)
            rewritten.elements = [self.optimize_expr(x) for x in expr.elements]
            return rewritten
        if isinstance(expr, ast.DictLiteral):
            rewritten = copy.deepcopy(expr)
            rewritten.entries = [(self.optimize_expr(k), self.optimize_expr(v)) for k, v in expr.entries]
            return rewritten
        if isinstance(expr, ast.Call):
            rewritten = copy.deepcopy(expr)
            rewritten.callee = self.optimize_expr(expr.callee)
            rewritten.args = [self.optimize_expr(a) for a in expr.args]
            return rewritten
        if isinstance(expr, ast.UnaryOp):
            operand = self.optimize_expr(expr.operand)
            if isinstance(operand, ast.Literal):
                folded = _fold_unary(expr.op, operand, expr.line, expr.col)
                if folded is not None:
                    return folded
            rewritten = copy.deepcopy(expr)
            rewritten.operand = operand
            return rewritten
        if isinstance(expr, ast.BinaryOp):
            left = self.optimize_expr(expr.left)
            right = self.optimize_expr(expr.right)
            if isinstance(left, ast.Literal) and isinstance(right, ast.Literal):
                folded = _fold_binary(expr.op, left, right, expr.line, expr.col)
                if folded is not None:
                    return folded
            rewritten = copy.deepcopy(expr)
            rewritten.left = left
            rewritten.right = right
            return rewritten
        return copy.deepcopy(expr)


def _block_terminal(stmts: list[ast.Stmt]) -> bool:
    if not stmts:
        return False
    return isinstance(stmts[-1], (ast.ReturnStmt, ast.BreakStmt, ast.ContinueStmt))


def _is_pure_expression(expr: ast.Expr) -> bool:
    if isinstance(expr, (ast.Literal, ast.Identifier)):
        return True
    if isinstance(expr, ast.MemberExpr):
        return _is_pure_expression(expr.target)
    if isinstance(expr, ast.IndexExpr):
        return _is_pure_expression(expr.target) and _is_pure_expression(expr.index)
    if isinstance(expr, ast.UnaryOp):
        return _is_pure_expression(expr.operand)
    if isinstance(expr, ast.BinaryOp):
        return _is_pure_expression(expr.left) and _is_pure_expression(expr.right)
    if isinstance(expr, ast.ListLiteral):
        return all(_is_pure_expression(x) for x in expr.elements)
    if isinstance(expr, ast.DictLiteral):
        return all(_is_pure_expression(k) and _is_pure_expression(v) for k, v in expr.entries)
    return False


def _fold_unary(op: str, operand: ast.Literal, line: int, col: int) -> ast.Literal | None:
    if op == "не" and operand.kind == "bool":
        return ast.Literal(line=line, col=col, value=not bool(operand.value), kind="bool")
    if op == "-" and operand.kind == "int":
        return ast.Literal(line=line, col=col, value=-int(operand.value), kind="int")
    if op == "-" and operand.kind == "float":
        return ast.Literal(line=line, col=col, value=-float(operand.value), kind="float")
    return None


def _fold_binary(op: str, left: ast.Literal, right: ast.Literal, line: int, col: int) -> ast.Literal | None:
    lv = left.value
    rv = right.value

    if op == "+" and left.kind == "int" and right.kind == "int":
        return ast.Literal(line=line, col=col, value=int(lv) + int(rv), kind="int")
    if op == "-" and left.kind == "int" and right.kind == "int":
        return ast.Literal(line=line, col=col, value=int(lv) - int(rv), kind="int")
    if op == "*" and left.kind == "int" and right.kind == "int":
        return ast.Literal(line=line, col=col, value=int(lv) * int(rv), kind="int")
    if op == "/" and left.kind == "int" and right.kind == "int" and int(rv) != 0:
        return ast.Literal(line=line, col=col, value=int(int(lv) / int(rv)), kind="int")
    if op == "%" and left.kind == "int" and right.kind == "int" and int(rv) != 0:
        return ast.Literal(line=line, col=col, value=int(lv) % int(rv), kind="int")

    if left.kind == "float" and right.kind == "float":
        if op == "+":
            return ast.Literal(line=line, col=col, value=float(lv) + float(rv), kind="float")
        if op == "-":
            return ast.Literal(line=line, col=col, value=float(lv) - float(rv), kind="float")
        if op == "*":
            return ast.Literal(line=line, col=col, value=float(lv) * float(rv), kind="float")
        if op == "/" and float(rv) != 0.0:
            return ast.Literal(line=line, col=col, value=float(lv) / float(rv), kind="float")
        if op == "%" and float(rv) != 0.0:
            return ast.Literal(line=line, col=col, value=float(lv) % float(rv), kind="float")

    if op == "+" and left.kind == "string" and right.kind == "string":
        return ast.Literal(line=line, col=col, value=str(lv) + str(rv), kind="string")

    if op in {"==", "!=", "<", "<=", ">", ">="}:
        if op == "==":
            v = lv == rv
        elif op == "!=":
            v = lv != rv
        elif op == "<":
            v = lv < rv
        elif op == "<=":
            v = lv <= rv
        elif op == ">":
            v = lv > rv
        else:
            v = lv >= rv
        return ast.Literal(line=line, col=col, value=bool(v), kind="bool")

    if op in {"и", "или"} and left.kind == "bool" and right.kind == "bool":
        if op == "и":
            v = bool(lv) and bool(rv)
        else:
            v = bool(lv) or bool(rv)
        return ast.Literal(line=line, col=col, value=v, kind="bool")

    return None


def _tree_shake(statements: list[ast.Stmt]) -> list[ast.Stmt]:
    functions: dict[str, ast.FuncDecl] = {}
    others: list[ast.Stmt] = []
    for stmt in statements:
        if isinstance(stmt, ast.FuncDecl):
            functions[stmt.name] = stmt
        else:
            others.append(stmt)

    if not functions:
        return statements

    reachable: set[str] = set()
    queue: list[str] = []

    if "main" in functions:
        queue.append("main")

    # Exported functions are public API for module imports/host calls and must survive DCE.
    for fn_name, fn in functions.items():
        if getattr(fn, "exported", False):
            queue.append(fn_name)

    for stmt in others:
        for callee in _collect_calls_in_stmt(stmt):
            if callee in functions and callee not in reachable:
                queue.append(callee)

    while queue:
        name = queue.pop()
        if name in reachable:
            continue
        reachable.add(name)
        fn = functions.get(name)
        if fn is None:
            continue
        for callee in _collect_calls_in_function(fn):
            if callee in functions and callee not in reachable:
                queue.append(callee)

    kept_funcs = [fn for fn_name, fn in functions.items() if fn_name in reachable]
    return others + kept_funcs


def _collect_calls_in_function(fn: ast.FuncDecl) -> set[str]:
    names: set[str] = set()
    for stmt in fn.body:
        names.update(_collect_calls_in_stmt(stmt))
    return names


def _collect_calls_in_stmt(stmt: ast.Stmt) -> set[str]:
    names: set[str] = set()
    if isinstance(stmt, ast.VarDecl):
        names.update(_collect_calls_in_expr(stmt.value))
    elif isinstance(stmt, ast.Assign):
        names.update(_collect_calls_in_expr(stmt.value))
    elif isinstance(stmt, ast.IndexAssign):
        names.update(_collect_calls_in_expr(stmt.target))
        names.update(_collect_calls_in_expr(stmt.index))
        names.update(_collect_calls_in_expr(stmt.value))
    elif isinstance(stmt, ast.IfStmt):
        names.update(_collect_calls_in_expr(stmt.condition))
        for s in stmt.then_body:
            names.update(_collect_calls_in_stmt(s))
        for s in stmt.else_body or []:
            names.update(_collect_calls_in_stmt(s))
    elif isinstance(stmt, ast.WhileStmt):
        names.update(_collect_calls_in_expr(stmt.condition))
        for s in stmt.body:
            names.update(_collect_calls_in_stmt(s))
    elif isinstance(stmt, ast.ForStmt):
        names.update(_collect_calls_in_expr(stmt.iterable))
        for s in stmt.body:
            names.update(_collect_calls_in_stmt(s))
    elif isinstance(stmt, ast.ReturnStmt):
        names.update(_collect_calls_in_expr(stmt.value))
    elif isinstance(stmt, ast.ExprStmt):
        names.update(_collect_calls_in_expr(stmt.expr))
    return names


def _collect_calls_in_expr(expr: ast.Expr) -> set[str]:
    names: set[str] = set()
    if isinstance(expr, ast.Call):
        if isinstance(expr.callee, ast.Identifier):
            names.add(expr.callee.name)
        else:
            names.update(_collect_calls_in_expr(expr.callee))
        for a in expr.args:
            names.update(_collect_calls_in_expr(a))
    elif isinstance(expr, ast.UnaryOp):
        names.update(_collect_calls_in_expr(expr.operand))
    elif isinstance(expr, ast.BinaryOp):
        names.update(_collect_calls_in_expr(expr.left))
        names.update(_collect_calls_in_expr(expr.right))
    elif isinstance(expr, ast.ListLiteral):
        for x in expr.elements:
            names.update(_collect_calls_in_expr(x))
    elif isinstance(expr, ast.DictLiteral):
        for k, v in expr.entries:
            names.update(_collect_calls_in_expr(k))
            names.update(_collect_calls_in_expr(v))
    elif isinstance(expr, ast.IndexExpr):
        names.update(_collect_calls_in_expr(expr.target))
        names.update(_collect_calls_in_expr(expr.index))
    elif isinstance(expr, ast.MemberExpr):
        names.update(_collect_calls_in_expr(expr.target))
    return names
