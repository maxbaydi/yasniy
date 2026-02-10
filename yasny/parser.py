from __future__ import annotations

from . import ast
from .diagnostics import YasnyError
from .lexer import Token


PRIMITIVE_TYPE_NAMES = {"Цел", "Дроб", "Лог", "Строка", "Пусто", "Любой", "Задача"}


class Parser:
    def __init__(self, tokens: list[Token], path: str | None = None):
        self.tokens = tokens
        self.path = path
        self.pos = 0

    def parse(self) -> ast.Program:
        statements: list[ast.Stmt] = []
        self._consume_newlines()
        while not self._check("EOF"):
            statements.append(self._parse_stmt())
            self._consume_newlines()
        return ast.Program(line=1, col=1, statements=statements)

    def _parse_stmt(self) -> ast.Stmt:
        tok = self._current()

        if tok.kind == "экспорт":
            return self._parse_export_stmt()
        if tok.kind == "пусть":
            return self._parse_var_decl(exported=False)
        if tok.kind == "асинхронная":
            return self._parse_async_func_decl(exported=False)
        if tok.kind == "функция":
            return self._parse_func_decl(exported=False, is_async=False)
        if tok.kind == "если":
            return self._parse_if_stmt()
        if tok.kind == "пока":
            return self._parse_while_stmt()
        if tok.kind == "для":
            return self._parse_for_stmt()
        if tok.kind == "подключить":
            return self._parse_import_all_stmt()
        if tok.kind == "из":
            return self._parse_import_from_stmt()
        if tok.kind == "вернуть":
            return self._parse_return_stmt()
        if tok.kind == "прервать":
            return self._parse_break_stmt()
        if tok.kind == "продолжить":
            return self._parse_continue_stmt()

        expr = self._parse_expr()
        if self._match("="):
            value = self._parse_expr()
            self._expect("NEWLINE", "Ожидался перевод строки после присваивания")
            if isinstance(expr, ast.Identifier):
                return ast.Assign(line=expr.line, col=expr.col, name=expr.name, value=value)
            if isinstance(expr, ast.IndexExpr):
                return ast.IndexAssign(line=expr.line, col=expr.col, target=expr.target, index=expr.index, value=value)
            raise YasnyError("Левая часть присваивания должна быть переменной или индексатором", expr.line, expr.col, self.path)

        self._expect("NEWLINE", "Ожидался перевод строки после выражения")
        return ast.ExprStmt(line=expr.line, col=expr.col, expr=expr)

    def _parse_export_stmt(self) -> ast.Stmt:
        start = self._expect("экспорт", "Ожидалось 'экспорт'")
        if self._check("пусть"):
            return self._parse_var_decl(exported=True)
        if self._check("асинхронная"):
            return self._parse_async_func_decl(exported=True)
        if self._check("функция"):
            return self._parse_func_decl(exported=True, is_async=False)
        raise YasnyError(
            "После 'экспорт' допускается только 'пусть', 'функция' или 'асинхронная функция'",
            start.line,
            start.col,
            self.path,
        )

    def _parse_import_all_stmt(self) -> ast.ImportAll:
        start = self._expect("подключить", "Ожидалось 'подключить'")
        path_tok = self._expect("STRING", "После 'подключить' ожидается строка с путём модуля")
        alias: str | None = None
        if self._match("как"):
            alias_tok = self._expect("IDENT", "После 'как' ожидается имя пространства имён")
            alias = str(alias_tok.value)
        self._expect("NEWLINE", "Ожидался перевод строки после оператора подключения")
        return ast.ImportAll(
            line=start.line,
            col=start.col,
            module_path=str(path_tok.value),
            alias=alias,
        )

    def _parse_import_from_stmt(self) -> ast.ImportFrom:
        start = self._expect("из", "Ожидалось 'из'")
        path_tok = self._expect("STRING", "После 'из' ожидается строка с путём модуля")
        self._expect("подключить", "Ожидалось 'подключить' после пути модуля")
        items: list[ast.ImportItem] = [self._parse_import_item()]
        while self._match(","):
            items.append(self._parse_import_item())
        self._expect("NEWLINE", "Ожидался перевод строки после оператора подключения")
        return ast.ImportFrom(
            line=start.line,
            col=start.col,
            module_path=str(path_tok.value),
            items=items,
        )

    def _parse_import_item(self) -> ast.ImportItem:
        name_tok = self._expect("IDENT", "Ожидалось имя символа для подключения")
        alias: str | None = None
        if self._match("как"):
            alias_tok = self._expect("IDENT", "После 'как' ожидается имя алиаса")
            alias = str(alias_tok.value)
        return ast.ImportItem(
            line=name_tok.line,
            col=name_tok.col,
            name=str(name_tok.value),
            alias=alias,
        )

    def _parse_var_decl(self, exported: bool) -> ast.VarDecl:
        start = self._expect("пусть", "Ожидалось 'пусть'")
        name_tok = self._expect("IDENT", "Ожидалось имя переменной")
        annotation: ast.TypeNode | None = None
        if self._match(":"):
            annotation = self._parse_type()
        self._expect("=", "Ожидался '=' в объявлении переменной")
        value = self._parse_expr()
        self._expect("NEWLINE", "Ожидался перевод строки после объявления переменной")
        return ast.VarDecl(
            line=start.line,
            col=start.col,
            name=str(name_tok.value),
            annotation=annotation,
            value=value,
            exported=exported,
        )

    def _parse_async_func_decl(self, exported: bool) -> ast.FuncDecl:
        start = self._expect("асинхронная", "Ожидалось 'асинхронная'")
        self._expect("функция", "После 'асинхронная' ожидалось 'функция'")
        return self._parse_func_decl_tail(start=start, exported=exported, is_async=True)

    def _parse_func_decl(self, exported: bool, is_async: bool) -> ast.FuncDecl:
        start = self._expect("функция", "Ожидалось 'функция'")
        return self._parse_func_decl_tail(start=start, exported=exported, is_async=is_async)

    def _parse_func_decl_tail(self, start: Token, exported: bool, is_async: bool) -> ast.FuncDecl:
        name_tok = self._expect("IDENT", "Ожидалось имя функции")
        self._expect("(", "Ожидался '(' в объявлении функции")
        params: list[ast.Param] = []
        if not self._check(")"):
            while True:
                p_name = self._expect("IDENT", "Ожидалось имя параметра")
                self._expect(":", "Ожидался ':' после имени параметра")
                p_type = self._parse_type()
                params.append(
                    ast.Param(
                        line=p_name.line,
                        col=p_name.col,
                        name=str(p_name.value),
                        type_node=p_type,
                    )
                )
                if not self._match(","):
                    break
        self._expect(")", "Ожидался ')' после параметров")
        self._consume_newlines()
        self._expect("->", "Ожидался '->' после параметров")
        self._consume_newlines()
        return_type = self._parse_type()
        self._consume_newlines()
        self._expect(":", "Ожидался ':' после типа возвращаемого значения")
        body = self._parse_block()
        return ast.FuncDecl(
            line=start.line,
            col=start.col,
            name=str(name_tok.value),
            params=params,
            return_type=return_type,
            body=body,
            exported=exported,
            is_async=is_async,
        )

    def _parse_if_stmt(self) -> ast.IfStmt:
        start = self._expect("если", "Ожидалось 'если'")
        condition = self._parse_expr()
        self._expect(":", "Ожидался ':' после условия")
        then_body = self._parse_block()
        else_body: list[ast.Stmt] | None = None
        if self._match("иначе"):
            self._expect(":", "Ожидался ':' после 'иначе'")
            else_body = self._parse_block()
        return ast.IfStmt(
            line=start.line,
            col=start.col,
            condition=condition,
            then_body=then_body,
            else_body=else_body,
        )

    def _parse_while_stmt(self) -> ast.WhileStmt:
        start = self._expect("пока", "Ожидалось 'пока'")
        condition = self._parse_expr()
        self._expect(":", "Ожидался ':' после условия цикла")
        body = self._parse_block()
        return ast.WhileStmt(line=start.line, col=start.col, condition=condition, body=body)

    def _parse_for_stmt(self) -> ast.ForStmt:
        start = self._expect("для", "Ожидалось 'для'")
        name_tok = self._expect("IDENT", "Ожидалось имя переменной цикла")
        self._expect("в", "Ожидалось 'в' в цикле for")
        iterable = self._parse_expr()
        self._expect(":", "Ожидался ':' после выражения цикла for")
        body = self._parse_block()
        return ast.ForStmt(
            line=start.line,
            col=start.col,
            var_name=str(name_tok.value),
            iterable=iterable,
            body=body,
        )

    def _parse_return_stmt(self) -> ast.ReturnStmt:
        start = self._expect("вернуть", "Ожидалось 'вернуть'")
        if self._check("NEWLINE"):
            raise YasnyError("После 'вернуть' ожидается выражение или 'пусто'", start.line, start.col, self.path)
        value = self._parse_expr()
        self._expect("NEWLINE", "Ожидался перевод строки после 'вернуть'")
        return ast.ReturnStmt(line=start.line, col=start.col, value=value)

    def _parse_break_stmt(self) -> ast.BreakStmt:
        tok = self._expect("прервать", "Ожидалось 'прервать'")
        self._expect("NEWLINE", "Ожидался перевод строки после 'прервать'")
        return ast.BreakStmt(line=tok.line, col=tok.col)

    def _parse_continue_stmt(self) -> ast.ContinueStmt:
        tok = self._expect("продолжить", "Ожидалось 'продолжить'")
        self._expect("NEWLINE", "Ожидался перевод строки после 'продолжить'")
        return ast.ContinueStmt(line=tok.line, col=tok.col)

    def _parse_block(self) -> list[ast.Stmt]:
        self._expect("NEWLINE", "Ожидался перевод строки после ':'")
        self._expect("INDENT", "Ожидался отступ блока")
        body: list[ast.Stmt] = []
        self._consume_newlines()
        while not self._check("DEDENT") and not self._check("EOF"):
            body.append(self._parse_stmt())
            self._consume_newlines()
        self._expect("DEDENT", "Ожидалось завершение блока")
        return body

    def _parse_type(self) -> ast.TypeNode:
        variants = [self._parse_type_atom()]
        while self._match("|"):
            variants.append(self._parse_type_atom())
        if len(variants) == 1:
            return variants[0]
        first = variants[0]
        return ast.UnionTypeNode(line=first.line, col=first.col, variants=variants)

    def _parse_type_atom(self) -> ast.TypeNode:
        tok = self._current()
        node: ast.TypeNode
        if tok.kind == "IDENT" and tok.value in PRIMITIVE_TYPE_NAMES:
            self._advance()
            node = ast.PrimitiveTypeNode(line=tok.line, col=tok.col, name=str(tok.value))
        elif tok.kind == "IDENT" and tok.value == "Список":
            self._advance()
            self._expect("[", "Ожидался '[' после 'Список'")
            element = self._parse_type()
            self._expect("]", "Ожидалась ']' после типа элемента списка")
            node = ast.ListTypeNode(line=tok.line, col=tok.col, element=element)
        elif tok.kind == "IDENT" and tok.value == "Словарь":
            self._advance()
            self._expect("[", "Ожидался '[' после 'Словарь'")
            key = self._parse_type()
            self._expect(",", "Ожидалась ',' между типами ключа и значения словаря")
            value = self._parse_type()
            self._expect("]", "Ожидалась ']' после типов словаря")
            node = ast.DictTypeNode(line=tok.line, col=tok.col, key=key, value=value)
        elif self._match("("):
            node = self._parse_type()
            self._expect(")", "Ожидалась ')' после типа")
        else:
            raise YasnyError("Ожидался тип", tok.line, tok.col, self.path)

        if self._match("?"):
            q = self._previous()
            null_t = ast.PrimitiveTypeNode(line=q.line, col=q.col, name="Пусто")
            return ast.UnionTypeNode(line=node.line, col=node.col, variants=[node, null_t])
        return node

    def _parse_expr(self) -> ast.Expr:
        return self._parse_or()

    def _parse_or(self) -> ast.Expr:
        expr = self._parse_and()
        while self._match("или"):
            op_tok = self._previous()
            right = self._parse_and()
            expr = ast.BinaryOp(line=op_tok.line, col=op_tok.col, left=expr, op="или", right=right)
        return expr

    def _parse_and(self) -> ast.Expr:
        expr = self._parse_comparison()
        while self._match("и"):
            op_tok = self._previous()
            right = self._parse_comparison()
            expr = ast.BinaryOp(line=op_tok.line, col=op_tok.col, left=expr, op="и", right=right)
        return expr

    def _parse_comparison(self) -> ast.Expr:
        expr = self._parse_add()
        while self._match("==", "!=", "<", "<=", ">", ">="):
            op_tok = self._previous()
            right = self._parse_add()
            expr = ast.BinaryOp(line=op_tok.line, col=op_tok.col, left=expr, op=op_tok.kind, right=right)
        return expr

    def _parse_add(self) -> ast.Expr:
        expr = self._parse_mul()
        while self._match("+", "-"):
            op_tok = self._previous()
            right = self._parse_mul()
            expr = ast.BinaryOp(line=op_tok.line, col=op_tok.col, left=expr, op=op_tok.kind, right=right)
        return expr

    def _parse_mul(self) -> ast.Expr:
        expr = self._parse_unary()
        while self._match("*", "/", "%"):
            op_tok = self._previous()
            right = self._parse_unary()
            expr = ast.BinaryOp(line=op_tok.line, col=op_tok.col, left=expr, op=op_tok.kind, right=right)
        return expr

    def _parse_unary(self) -> ast.Expr:
        if self._match("ждать"):
            op_tok = self._previous()
            operand = self._parse_unary()
            return ast.AwaitExpr(line=op_tok.line, col=op_tok.col, operand=operand)
        if self._match("не", "-"):
            op_tok = self._previous()
            operand = self._parse_unary()
            return ast.UnaryOp(line=op_tok.line, col=op_tok.col, op=op_tok.kind, operand=operand)
        return self._parse_postfix()

    def _parse_postfix(self) -> ast.Expr:
        expr = self._parse_primary()
        while True:
            if self._match("("):
                lpar = self._previous()
                args: list[ast.Expr] = []
                if not self._check(")"):
                    while True:
                        args.append(self._parse_expr())
                        if not self._match(","):
                            break
                self._expect(")", "Ожидалась ')' после аргументов")
                expr = ast.Call(line=lpar.line, col=lpar.col, callee=expr, args=args)
                continue

            if self._match("["):
                lbr = self._previous()
                idx = self._parse_expr()
                self._expect("]", "Ожидалась ']' после индексатора")
                expr = ast.IndexExpr(line=lbr.line, col=lbr.col, target=expr, index=idx)
                continue

            if self._match("."):
                dot = self._previous()
                member = self._expect("IDENT", "Ожидалось имя члена после '.'")
                expr = ast.MemberExpr(line=dot.line, col=dot.col, target=expr, member=str(member.value))
                continue

            break
        return expr

    def _parse_primary(self) -> ast.Expr:
        tok = self._current()

        if self._match("INT"):
            t = self._previous()
            return ast.Literal(line=t.line, col=t.col, value=t.value, kind="int")
        if self._match("FLOAT"):
            t = self._previous()
            return ast.Literal(line=t.line, col=t.col, value=t.value, kind="float")
        if self._match("STRING"):
            t = self._previous()
            return ast.Literal(line=t.line, col=t.col, value=t.value, kind="string")
        if self._match("истина"):
            t = self._previous()
            return ast.Literal(line=t.line, col=t.col, value=True, kind="bool")
        if self._match("ложь"):
            t = self._previous()
            return ast.Literal(line=t.line, col=t.col, value=False, kind="bool")
        if self._match("пусто"):
            t = self._previous()
            return ast.Literal(line=t.line, col=t.col, value=None, kind="null")

        if self._match("IDENT"):
            t = self._previous()
            return ast.Identifier(line=t.line, col=t.col, name=str(t.value))

        if self._match("("):
            expr = self._parse_expr()
            self._expect(")", "Ожидалась ')' после выражения")
            return expr

        if self._match("["):
            lbr = self._previous()
            elements: list[ast.Expr] = []
            if not self._check("]"):
                while True:
                    elements.append(self._parse_expr())
                    if not self._match(","):
                        break
            self._expect("]", "Ожидалась ']' после литерала списка")
            return ast.ListLiteral(line=lbr.line, col=lbr.col, elements=elements)

        if self._match("{"):
            lbr = self._previous()
            entries: list[tuple[ast.Expr, ast.Expr]] = []
            if not self._check("}"):
                while True:
                    key = self._parse_expr()
                    self._expect(":", "Ожидался ':' между ключом и значением словаря")
                    value = self._parse_expr()
                    entries.append((key, value))
                    if not self._match(","):
                        break
            self._expect("}", "Ожидалась '}' после литерала словаря")
            return ast.DictLiteral(line=lbr.line, col=lbr.col, entries=entries)

        raise YasnyError("Ожидалось выражение", tok.line, tok.col, self.path)

    def _consume_newlines(self) -> None:
        while self._match("NEWLINE"):
            pass

    def _current(self) -> Token:
        return self.tokens[self.pos]

    def _peek(self, offset: int) -> Token:
        idx = self.pos + offset
        if idx >= len(self.tokens):
            return self.tokens[-1]
        return self.tokens[idx]

    def _previous(self) -> Token:
        return self.tokens[self.pos - 1]

    def _advance(self) -> Token:
        if not self._check("EOF"):
            self.pos += 1
        return self._previous()

    def _check(self, kind: str) -> bool:
        return self._current().kind == kind

    def _match(self, *kinds: str) -> bool:
        if self._current().kind in kinds:
            self._advance()
            return True
        return False

    def _expect(self, kind: str, message: str) -> Token:
        if self._check(kind):
            return self._advance()
        tok = self._current()
        raise YasnyError(message, tok.line, tok.col, self.path)
