namespace YasnNative.Core;

public sealed class Parser
{
    private static readonly HashSet<string> PrimitiveTypeNames = new(StringComparer.Ordinal)
    {
        "Цел",
        "Дроб",
        "Лог",
        "Строка",
        "Пусто",
        "Любой",
        "Задача",
    };

    private readonly List<Token> _tokens;
    private readonly string? _path;
    private int _pos;

    public Parser(List<Token> tokens, string? path = null)
    {
        _tokens = tokens;
        _path = path;
    }

    public ProgramNode Parse()
    {
        var statements = new List<Stmt>();
        ConsumeNewlines();
        while (!Check("EOF"))
        {
            statements.Add(ParseStmt());
            ConsumeNewlines();
        }

        return new ProgramNode(1, 1, statements);
    }

    private Stmt ParseStmt()
    {
        var tok = Current();

        return tok.Kind switch
        {
            "экспорт" => ParseExportStmt(),
            "пусть" => ParseVarDecl(exported: false),
            "асинхронная" => ParseAsyncFuncDecl(exported: false),
            "функция" => ParseFuncDecl(exported: false, isAsync: false),
            "если" => ParseIfStmt(),
            "пока" => ParseWhileStmt(),
            "для" => ParseForStmt(),
            "подключить" => ParseImportAllStmt(),
            "из" => ParseImportFromStmt(),
            "вернуть" => ParseReturnStmt(),
            "прервать" => ParseBreakStmt(),
            "продолжить" => ParseContinueStmt(),
            _ => ParseExprStatementOrAssign(),
        };
    }

    private Stmt ParseExprStatementOrAssign()
    {
        var expr = ParseExpr();
        if (Match("="))
        {
            var value = ParseExpr();
            Expect("NEWLINE", "Ожидался перевод строки после присваивания");

            return expr switch
            {
                IdentifierExpr id => new AssignStmt(id.Line, id.Col, id.Name, value),
                IndexExpr idx => new IndexAssignStmt(idx.Line, idx.Col, idx.Target, idx.Index, value),
                _ => throw YasnException.At(
                    "Левая часть присваивания должна быть переменной или индексатором",
                    expr.Line,
                    expr.Col,
                    _path),
            };
        }

        Expect("NEWLINE", "Ожидался перевод строки после выражения");
        return new ExprStmt(expr.Line, expr.Col, expr);
    }

    private Stmt ParseExportStmt()
    {
        var start = Expect("экспорт", "Ожидалось 'экспорт'");
        if (Check("пусть"))
        {
            return ParseVarDecl(exported: true);
        }

        if (Check("асинхронная"))
        {
            return ParseAsyncFuncDecl(exported: true);
        }

        if (Check("функция"))
        {
            return ParseFuncDecl(exported: true, isAsync: false);
        }

        throw YasnException.At(
            "После 'экспорт' допускается только 'пусть', 'функция' или 'асинхронная функция'",
            start.Line,
            start.Col,
            _path);
    }

    private ImportAllStmt ParseImportAllStmt()
    {
        var start = Expect("подключить", "Ожидалось 'подключить'");
        var pathToken = Expect("STRING", "После 'подключить' ожидается строка с путём модуля");
        string? alias = null;
        if (Match("как"))
        {
            var aliasToken = Expect("IDENT", "После 'как' ожидается имя пространства имён");
            alias = (string)aliasToken.Value!;
        }

        Expect("NEWLINE", "Ожидался перевод строки после оператора подключения");
        return new ImportAllStmt(start.Line, start.Col, (string)pathToken.Value!, alias);
    }

    private ImportFromStmt ParseImportFromStmt()
    {
        var start = Expect("из", "Ожидалось 'из'");
        var pathToken = Expect("STRING", "После 'из' ожидается строка с путём модуля");
        Expect("подключить", "Ожидалось 'подключить' после пути модуля");

        var items = new List<ImportItemNode> { ParseImportItem() };
        while (Match(","))
        {
            items.Add(ParseImportItem());
        }

        Expect("NEWLINE", "Ожидался перевод строки после оператора подключения");
        return new ImportFromStmt(start.Line, start.Col, (string)pathToken.Value!, items);
    }

    private ImportItemNode ParseImportItem()
    {
        var nameToken = Expect("IDENT", "Ожидалось имя символа для подключения");
        string? alias = null;
        if (Match("как"))
        {
            var aliasToken = Expect("IDENT", "После 'как' ожидается имя алиаса");
            alias = (string)aliasToken.Value!;
        }

        return new ImportItemNode(nameToken.Line, nameToken.Col, (string)nameToken.Value!, alias);
    }

    private VarDeclStmt ParseVarDecl(bool exported)
    {
        var start = Expect("пусть", "Ожидалось 'пусть'");
        var nameToken = Expect("IDENT", "Ожидалось имя переменной");
        TypeNode? annotation = null;
        if (Match(":"))
        {
            annotation = ParseType();
        }

        Expect("=", "Ожидался '=' в объявлении переменной");
        var value = ParseExpr();
        Expect("NEWLINE", "Ожидался перевод строки после объявления переменной");
        return new VarDeclStmt(start.Line, start.Col, (string)nameToken.Value!, annotation, value, exported);
    }

    private FuncDeclStmt ParseAsyncFuncDecl(bool exported)
    {
        var start = Expect("асинхронная", "Ожидалось 'асинхронная'");
        Expect("функция", "После 'асинхронная' ожидалось 'функция'");
        return ParseFuncDeclTail(start, exported, isAsync: true);
    }

    private FuncDeclStmt ParseFuncDecl(bool exported, bool isAsync)
    {
        var start = Expect("функция", "Ожидалось 'функция'");
        return ParseFuncDeclTail(start, exported, isAsync);
    }

    private FuncDeclStmt ParseFuncDeclTail(Token start, bool exported, bool isAsync)
    {
        var nameToken = Expect("IDENT", "Ожидалось имя функции");
        Expect("(", "Ожидался '(' в объявлении функции");

        var parameters = new List<ParamNode>();
        if (!Check(")"))
        {
            while (true)
            {
                var pName = Expect("IDENT", "Ожидалось имя параметра");
                Expect(":", "Ожидался ':' после имени параметра");
                var pType = ParseType();
                parameters.Add(new ParamNode(pName.Line, pName.Col, (string)pName.Value!, pType));
                if (!Match(","))
                {
                    break;
                }
            }
        }

        Expect(")", "Ожидалась ')' после параметров");
        ConsumeNewlines();
        Expect("->", "Ожидался '->' после параметров");
        ConsumeNewlines();
        var returnType = ParseType();
        ConsumeNewlines();
        Expect(":", "Ожидался ':' после типа возвращаемого значения");
        var body = ParseBlock();

        return new FuncDeclStmt(
            start.Line,
            start.Col,
            (string)nameToken.Value!,
            parameters,
            returnType,
            body,
            exported,
            isAsync);
    }

    private IfStmt ParseIfStmt()
    {
        var start = Expect("если", "Ожидалось 'если'");
        var condition = ParseExpr();
        Expect(":", "Ожидался ':' после условия");
        var thenBody = ParseBlock();

        List<Stmt>? elseBody = null;
        if (Match("иначе"))
        {
            Expect(":", "Ожидался ':' после 'иначе'");
            elseBody = ParseBlock();
        }

        return new IfStmt(start.Line, start.Col, condition, thenBody, elseBody);
    }

    private WhileStmt ParseWhileStmt()
    {
        var start = Expect("пока", "Ожидалось 'пока'");
        var condition = ParseExpr();
        Expect(":", "Ожидался ':' после условия цикла");
        var body = ParseBlock();
        return new WhileStmt(start.Line, start.Col, condition, body);
    }

    private ForStmt ParseForStmt()
    {
        var start = Expect("для", "Ожидалось 'для'");
        var nameToken = Expect("IDENT", "Ожидалось имя переменной цикла");
        Expect("в", "Ожидалось 'в' в цикле for");
        var iterable = ParseExpr();
        Expect(":", "Ожидался ':' после выражения цикла for");
        var body = ParseBlock();
        return new ForStmt(start.Line, start.Col, (string)nameToken.Value!, iterable, body);
    }

    private ReturnStmt ParseReturnStmt()
    {
        var start = Expect("вернуть", "Ожидалось 'вернуть'");
        if (Check("NEWLINE"))
        {
            throw YasnException.At("После 'вернуть' ожидается выражение или 'пусто'", start.Line, start.Col, _path);
        }

        var value = ParseExpr();
        Expect("NEWLINE", "Ожидался перевод строки после 'вернуть'");
        return new ReturnStmt(start.Line, start.Col, value);
    }

    private BreakStmt ParseBreakStmt()
    {
        var tok = Expect("прервать", "Ожидалось 'прервать'");
        Expect("NEWLINE", "Ожидался перевод строки после 'прервать'");
        return new BreakStmt(tok.Line, tok.Col);
    }

    private ContinueStmt ParseContinueStmt()
    {
        var tok = Expect("продолжить", "Ожидалось 'продолжить'");
        Expect("NEWLINE", "Ожидался перевод строки после 'продолжить'");
        return new ContinueStmt(tok.Line, tok.Col);
    }

    private List<Stmt> ParseBlock()
    {
        Expect("NEWLINE", "Ожидался перевод строки после ':'");
        Expect("INDENT", "Ожидался отступ блока");
        var body = new List<Stmt>();

        ConsumeNewlines();
        while (!Check("DEDENT") && !Check("EOF"))
        {
            body.Add(ParseStmt());
            ConsumeNewlines();
        }

        Expect("DEDENT", "Ожидалось завершение блока");
        return body;
    }

    private TypeNode ParseType()
    {
        var variants = new List<TypeNode> { ParseTypeAtom() };
        while (Match("|"))
        {
            variants.Add(ParseTypeAtom());
        }

        if (variants.Count == 1)
        {
            return variants[0];
        }

        return new UnionTypeNode(variants[0].Line, variants[0].Col, variants);
    }

    private TypeNode ParseTypeAtom()
    {
        var tok = Current();
        TypeNode node;

        if (tok.Kind == "IDENT" && tok.Value is string ident && PrimitiveTypeNames.Contains(ident))
        {
            Advance();
            node = new PrimitiveTypeNode(tok.Line, tok.Col, ident);
        }
        else if (tok.Kind == "IDENT" && Equals(tok.Value, "Список"))
        {
            Advance();
            Expect("[", "Ожидался '[' после 'Список'");
            var element = ParseType();
            Expect("]", "Ожидалась ']' после типа элемента списка");
            node = new ListTypeNode(tok.Line, tok.Col, element);
        }
        else if (tok.Kind == "IDENT" && Equals(tok.Value, "Словарь"))
        {
            Advance();
            Expect("[", "Ожидался '[' после 'Словарь'");
            var key = ParseType();
            Expect(",", "Ожидалась ',' между типами ключа и значения словаря");
            var value = ParseType();
            Expect("]", "Ожидалась ']' после типов словаря");
            node = new DictTypeNode(tok.Line, tok.Col, key, value);
        }
        else if (Match("("))
        {
            node = ParseType();
            Expect(")", "Ожидалась ')' после типа");
        }
        else
        {
            throw YasnException.At("Ожидался тип", tok.Line, tok.Col, _path);
        }

        if (Match("?"))
        {
            var q = Previous();
            var nullType = new PrimitiveTypeNode(q.Line, q.Col, "Пусто");
            return new UnionTypeNode(node.Line, node.Col, [node, nullType]);
        }

        return node;
    }

    private Expr ParseExpr()
    {
        return ParseOr();
    }

    private Expr ParseOr()
    {
        var expr = ParseAnd();
        while (Match("или"))
        {
            var op = Previous();
            var right = ParseAnd();
            expr = new BinaryExpr(op.Line, op.Col, expr, "или", right);
        }

        return expr;
    }

    private Expr ParseAnd()
    {
        var expr = ParseComparison();
        while (Match("и"))
        {
            var op = Previous();
            var right = ParseComparison();
            expr = new BinaryExpr(op.Line, op.Col, expr, "и", right);
        }

        return expr;
    }

    private Expr ParseComparison()
    {
        var expr = ParseAdd();
        while (Match("==", "!=", "<", "<=", ">", ">="))
        {
            var op = Previous();
            var right = ParseAdd();
            expr = new BinaryExpr(op.Line, op.Col, expr, op.Kind, right);
        }

        return expr;
    }

    private Expr ParseAdd()
    {
        var expr = ParseMul();
        while (Match("+", "-"))
        {
            var op = Previous();
            var right = ParseMul();
            expr = new BinaryExpr(op.Line, op.Col, expr, op.Kind, right);
        }

        return expr;
    }

    private Expr ParseMul()
    {
        var expr = ParseUnary();
        while (Match("*", "/", "%"))
        {
            var op = Previous();
            var right = ParseUnary();
            expr = new BinaryExpr(op.Line, op.Col, expr, op.Kind, right);
        }

        return expr;
    }

    private Expr ParseUnary()
    {
        if (Match("ждать"))
        {
            var op = Previous();
            var operand = ParseUnary();
            return new AwaitExpr(op.Line, op.Col, operand);
        }

        if (Match("не", "-"))
        {
            var op = Previous();
            var operand = ParseUnary();
            return new UnaryExpr(op.Line, op.Col, op.Kind, operand);
        }

        return ParsePostfix();
    }

    private Expr ParsePostfix()
    {
        var expr = ParsePrimary();

        while (true)
        {
            if (Match("("))
            {
                var lpar = Previous();
                var args = new List<Expr>();
                if (!Check(")"))
                {
                    while (true)
                    {
                        args.Add(ParseExpr());
                        if (!Match(","))
                        {
                            break;
                        }
                    }
                }

                Expect(")", "Ожидалась ')' после аргументов");
                expr = new CallExpr(lpar.Line, lpar.Col, expr, args);
                continue;
            }

            if (Match("["))
            {
                var lbr = Previous();
                var indexExpr = ParseExpr();
                Expect("]", "Ожидалась ']' после индексатора");
                expr = new IndexExpr(lbr.Line, lbr.Col, expr, indexExpr);
                continue;
            }

            if (Match("."))
            {
                var dot = Previous();
                var member = Expect("IDENT", "Ожидалось имя члена после '.'");
                expr = new MemberExpr(dot.Line, dot.Col, expr, (string)member.Value!);
                continue;
            }

            break;
        }

        return expr;
    }

    private Expr ParsePrimary()
    {
        var tok = Current();

        if (Match("INT"))
        {
            var t = Previous();
            return new LiteralExpr(t.Line, t.Col, t.Value, "int");
        }

        if (Match("FLOAT"))
        {
            var t = Previous();
            return new LiteralExpr(t.Line, t.Col, t.Value, "float");
        }

        if (Match("STRING"))
        {
            var t = Previous();
            return new LiteralExpr(t.Line, t.Col, t.Value, "string");
        }

        if (Match("истина"))
        {
            var t = Previous();
            return new LiteralExpr(t.Line, t.Col, true, "bool");
        }

        if (Match("ложь"))
        {
            var t = Previous();
            return new LiteralExpr(t.Line, t.Col, false, "bool");
        }

        if (Match("пусто"))
        {
            var t = Previous();
            return new LiteralExpr(t.Line, t.Col, null, "null");
        }

        if (Match("IDENT"))
        {
            var t = Previous();
            return new IdentifierExpr(t.Line, t.Col, (string)t.Value!);
        }

        if (Match("("))
        {
            var expr = ParseExpr();
            Expect(")", "Ожидалась ')' после выражения");
            return expr;
        }

        if (Match("["))
        {
            var left = Previous();
            var elements = new List<Expr>();
            if (!Check("]"))
            {
                while (true)
                {
                    elements.Add(ParseExpr());
                    if (!Match(","))
                    {
                        break;
                    }
                }
            }

            Expect("]", "Ожидалась ']' после литерала списка");
            return new ListLiteralExpr(left.Line, left.Col, elements);
        }

        if (Match("{"))
        {
            var left = Previous();
            var entries = new List<(Expr Key, Expr Value)>();
            if (!Check("}"))
            {
                while (true)
                {
                    var key = ParseExpr();
                    Expect(":", "Ожидался ':' между ключом и значением словаря");
                    var value = ParseExpr();
                    entries.Add((key, value));
                    if (!Match(","))
                    {
                        break;
                    }
                }
            }

            Expect("}", "Ожидалась '}' после литерала словаря");
            return new DictLiteralExpr(left.Line, left.Col, entries);
        }

        throw YasnException.At("Ожидалось выражение", tok.Line, tok.Col, _path);
    }

    private void ConsumeNewlines()
    {
        while (Match("NEWLINE"))
        {
        }
    }

    private Token Current()
    {
        return _tokens[_pos];
    }

    private Token Previous()
    {
        return _tokens[_pos - 1];
    }

    private Token Advance()
    {
        if (!Check("EOF"))
        {
            _pos++;
        }

        return Previous();
    }

    private bool Check(string kind)
    {
        return Current().Kind == kind;
    }

    private bool Match(params string[] kinds)
    {
        var currentKind = Current().Kind;
        for (var i = 0; i < kinds.Length; i++)
        {
            if (currentKind == kinds[i])
            {
                Advance();
                return true;
            }
        }

        return false;
    }

    private Token Expect(string kind, string message)
    {
        if (Check(kind))
        {
            return Advance();
        }

        var tok = Current();
        throw YasnException.At(message, tok.Line, tok.Col, _path);
    }
}
