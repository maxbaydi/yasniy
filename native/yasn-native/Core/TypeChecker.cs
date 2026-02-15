namespace YasnNative.Core;

public static class TypeChecker
{
    private static readonly TypeRef IntType = TypeRef.Primitive("Цел");
    private static readonly TypeRef FloatType = TypeRef.Primitive("Дроб");
    private static readonly TypeRef BoolType = TypeRef.Primitive("Лог");
    private static readonly TypeRef StringType = TypeRef.Primitive("Строка");
    private static readonly TypeRef NullType = TypeRef.Primitive("Пусто");
    private static readonly TypeRef AnyType = TypeRef.Primitive("Любой");
    private static readonly TypeRef TaskType = TypeRef.Primitive("Задача");
    private static readonly TypeRef VoidType = NullType;

    private static readonly HashSet<string> PrimitiveNames = new(StringComparer.Ordinal)
    {
        "Цел",
        "Дроб",
        "Лог",
        "Строка",
        "Пусто",
        "Любой",
        "Задача",
    };

    public static void Check(ProgramNode program, string? path = null)
    {
        var signatures = BuildFunctionSignatures(program, path);
        ValidateMainSignature(signatures, path);

        var globals = new Dictionary<string, TypeRef>(StringComparer.Ordinal);
        foreach (var stmt in program.Statements)
        {
            if (stmt is VarDeclStmt varDecl)
            {
                if (!globals.TryAdd(varDecl.Name, AnyType))
                {
                    throw YasnException.At($"Глобальная переменная '{varDecl.Name}' уже объявлена", varDecl.Line, varDecl.Col, path);
                }
            }
        }

        var root = Scope.CreateRoot(globals);
        var context = new CheckContext(path, signatures);

        foreach (var stmt in program.Statements)
        {
            if (stmt is FuncDeclStmt)
            {
                continue;
            }

            CheckStmt(stmt, root, context, expectedReturn: null, loopDepth: 0);
        }

        foreach (var stmt in program.Statements)
        {
            if (stmt is not FuncDeclStmt fn)
            {
                continue;
            }

            var fnScope = Scope.CreateChild(root);
            foreach (var param in fn.Params)
            {
                var paramType = FromTypeNode(param.TypeNode, path);
                fnScope.Declare(param.Name, paramType, param.Line, param.Col, path);
            }

            var returnType = FromTypeNode(fn.ReturnType, path);
            foreach (var bodyStmt in fn.Body)
            {
                CheckStmt(bodyStmt, fnScope, context, returnType, loopDepth: 0);
            }
        }
    }

    private static Dictionary<string, FuncSignature> BuildFunctionSignatures(ProgramNode program, string? path)
    {
        var signatures = new Dictionary<string, FuncSignature>(StringComparer.Ordinal);
        foreach (var stmt in program.Statements)
        {
            if (stmt is not FuncDeclStmt fn)
            {
                continue;
            }

            if (signatures.ContainsKey(fn.Name))
            {
                throw YasnException.At($"Функция '{fn.Name}' уже объявлена", fn.Line, fn.Col, path);
            }

            var parameters = fn.Params
                .Select(param => FromTypeNode(param.TypeNode, path))
                .ToList();
            var returnType = FromTypeNode(fn.ReturnType, path);
            signatures[fn.Name] = new FuncSignature(fn.Name, parameters, returnType, fn.IsAsync);
        }

        return signatures;
    }

    private static void ValidateMainSignature(Dictionary<string, FuncSignature> signatures, string? path)
    {
        if (!signatures.TryGetValue("main", out var main))
        {
            return;
        }

        if (main.IsAsync)
        {
            throw new YasnException("'main' не может быть асинхронной функцией", path: path);
        }

        if (main.ParamTypes.Count != 0)
        {
            throw new YasnException("'main' должна быть без параметров", path: path);
        }

        if (!TypeEquals(main.ReturnType, VoidType))
        {
            throw new YasnException("'main' должна иметь возвращаемый тип Пусто", path: path);
        }
    }

    private static void CheckStmt(
        Stmt stmt,
        Scope scope,
        CheckContext context,
        TypeRef? expectedReturn,
        int loopDepth)
    {
        switch (stmt)
        {
            case ImportAllStmt:
            case ImportFromStmt:
                return;

            case VarDeclStmt varDecl:
            {
                var valueType = CheckExpr(varDecl.Value, scope, context);
                var declared = varDecl.Annotation is null ? valueType : FromTypeNode(varDecl.Annotation, context.Path);
                if (varDecl.Annotation is not null && !IsAssignable(valueType, declared))
                {
                    throw YasnException.At(
                        $"Тип выражения '{FormatType(valueType)}' не совместим с объявленным типом '{FormatType(declared)}'",
                        varDecl.Line,
                        varDecl.Col,
                        context.Path);
                }

                scope.AssignOrDeclare(varDecl.Name, declared, varDecl.Line, varDecl.Col, context.Path);
                return;
            }

            case AssignStmt assign:
            {
                var targetType = scope.Resolve(assign.Name, assign.Line, assign.Col, context.Path);
                var valueType = CheckExpr(assign.Value, scope, context);
                if (!IsAssignable(valueType, targetType))
                {
                    throw YasnException.At(
                        $"Нельзя присвоить '{FormatType(valueType)}' в переменную '{assign.Name}' типа '{FormatType(targetType)}'",
                        assign.Line,
                        assign.Col,
                        context.Path);
                }

                return;
            }

            case IndexAssignStmt indexAssign:
            {
                var targetType = CheckExpr(indexAssign.Target, scope, context);
                var indexType = CheckExpr(indexAssign.Index, scope, context);
                var valueType = CheckExpr(indexAssign.Value, scope, context);
                ValidateIndexSet(targetType, indexType, valueType, indexAssign.Line, indexAssign.Col, context.Path);
                return;
            }

            case IfStmt ifStmt:
            {
                var conditionType = CheckExpr(ifStmt.Condition, scope, context);
                EnsureBooleanCondition(conditionType, ifStmt.Condition.Line, ifStmt.Condition.Col, context.Path);

                var thenScope = Scope.CreateChild(scope);
                foreach (var nested in ifStmt.ThenBody)
                {
                    CheckStmt(nested, thenScope, context, expectedReturn, loopDepth);
                }

                if (ifStmt.ElseBody is not null)
                {
                    var elseScope = Scope.CreateChild(scope);
                    foreach (var nested in ifStmt.ElseBody)
                    {
                        CheckStmt(nested, elseScope, context, expectedReturn, loopDepth);
                    }
                }

                return;
            }

            case WhileStmt whileStmt:
            {
                var conditionType = CheckExpr(whileStmt.Condition, scope, context);
                EnsureBooleanCondition(conditionType, whileStmt.Condition.Line, whileStmt.Condition.Col, context.Path);

                var whileScope = Scope.CreateChild(scope);
                foreach (var nested in whileStmt.Body)
                {
                    CheckStmt(nested, whileScope, context, expectedReturn, loopDepth + 1);
                }

                return;
            }

            case ForStmt forStmt:
            {
                var iterableType = CheckExpr(forStmt.Iterable, scope, context);
                var itemType = GetIterableItemType(iterableType, forStmt.Line, forStmt.Col, context.Path);
                var forScope = Scope.CreateChild(scope);
                forScope.Declare(forStmt.VarName, itemType, forStmt.Line, forStmt.Col, context.Path);
                foreach (var nested in forStmt.Body)
                {
                    CheckStmt(nested, forScope, context, expectedReturn, loopDepth + 1);
                }

                return;
            }

            case ReturnStmt ret:
            {
                if (expectedReturn is null)
                {
                    throw YasnException.At("'вернуть' допустим только внутри функции", ret.Line, ret.Col, context.Path);
                }

                var valueType = CheckExpr(ret.Value, scope, context);
                if (!IsAssignable(valueType, expectedReturn))
                {
                    throw YasnException.At(
                        $"Возвращаемый тип '{FormatType(valueType)}' не совместим с ожидаемым '{FormatType(expectedReturn)}'",
                        ret.Line,
                        ret.Col,
                        context.Path);
                }

                return;
            }

            case BreakStmt:
                if (loopDepth <= 0)
                {
                    throw YasnException.At("'прервать' допустим только внутри цикла", stmt.Line, stmt.Col, context.Path);
                }

                return;

            case ContinueStmt:
                if (loopDepth <= 0)
                {
                    throw YasnException.At("'продолжить' допустим только внутри цикла", stmt.Line, stmt.Col, context.Path);
                }

                return;

            case ExprStmt exprStmt:
                _ = CheckExpr(exprStmt.Expr, scope, context);
                return;

            case FuncDeclStmt:
                throw YasnException.At("Вложенные функции не поддерживаются", stmt.Line, stmt.Col, context.Path);

            default:
                throw YasnException.At("Неизвестный оператор для type checker", stmt.Line, stmt.Col, context.Path);
        }
    }

    private static TypeRef CheckExpr(Expr expr, Scope scope, CheckContext context)
    {
        switch (expr)
        {
            case LiteralExpr literal:
                return InferLiteralType(literal);

            case IdentifierExpr ident:
                return scope.Resolve(ident.Name, ident.Line, ident.Col, context.Path);

            case ListLiteralExpr list:
            {
                if (list.Elements.Count == 0)
                {
                    return TypeRef.List(AnyType);
                }

                var elementTypes = list.Elements.Select(element => CheckExpr(element, scope, context)).ToList();
                return TypeRef.List(UnionOf(elementTypes));
            }

            case DictLiteralExpr dict:
            {
                if (dict.Entries.Count == 0)
                {
                    return TypeRef.Dict(AnyType, AnyType);
                }

                var keyTypes = dict.Entries.Select(entry => CheckExpr(entry.Key, scope, context)).ToList();
                var valueTypes = dict.Entries.Select(entry => CheckExpr(entry.Value, scope, context)).ToList();
                return TypeRef.Dict(UnionOf(keyTypes), UnionOf(valueTypes));
            }

            case MemberExpr member:
            {
                var targetType = CheckExpr(member.Target, scope, context);
                if (IsAnyType(targetType))
                {
                    return AnyType;
                }

                if (TryGetDict(targetType, out var dictKeyType, out var dictValueType))
                {
                    if (!IsAssignable(StringType, dictKeyType))
                    {
                        throw YasnException.At(
                            $"Доступ через '.' требует ключ типа Строка, словарь имеет ключ '{FormatType(dictKeyType)}'",
                            member.Line,
                            member.Col,
                            context.Path);
                    }

                    return dictValueType;
                }

                throw YasnException.At(
                    $"Оператор '.' не поддерживается для типа '{FormatType(targetType)}'",
                    member.Line,
                    member.Col,
                    context.Path);
            }

            case IndexExpr indexExpr:
            {
                var targetType = CheckExpr(indexExpr.Target, scope, context);
                var indexType = CheckExpr(indexExpr.Index, scope, context);
                return ValidateIndexGet(targetType, indexType, indexExpr.Line, indexExpr.Col, context.Path);
            }

            case UnaryExpr unary:
            {
                var operandType = CheckExpr(unary.Operand, scope, context);
                if (unary.Op == "не")
                {
                    EnsureBooleanCondition(operandType, unary.Line, unary.Col, context.Path);
                    return BoolType;
                }

                if (unary.Op == "-")
                {
                    if (!IsNumericType(operandType))
                    {
                        throw YasnException.At("Унарный '-' применим только к числам", unary.Line, unary.Col, context.Path);
                    }

                    return operandType;
                }

                throw YasnException.At($"Неизвестный унарный оператор: {unary.Op}", unary.Line, unary.Col, context.Path);
            }

            case AwaitExpr awaitExpr:
            {
                var operandType = CheckExpr(awaitExpr.Operand, scope, context);
                if (!IsAssignable(operandType, TaskType))
                {
                    throw YasnException.At("'ждать' применим только к типу Задача", awaitExpr.Line, awaitExpr.Col, context.Path);
                }

                return AnyType;
            }

            case BinaryExpr binary:
                return CheckBinaryExpr(binary, scope, context);

            case CallExpr call:
                return CheckCallExpr(call, scope, context);

            default:
                throw YasnException.At("Неизвестный тип выражения для type checker", expr.Line, expr.Col, context.Path);
        }
    }

    private static TypeRef CheckBinaryExpr(BinaryExpr binary, Scope scope, CheckContext context)
    {
        var left = CheckExpr(binary.Left, scope, context);
        var right = CheckExpr(binary.Right, scope, context);

        if (IsAnyType(left) || IsAnyType(right))
        {
            return binary.Op is "==" or "!=" or "<" or "<=" or ">" or ">=" or "и" or "или"
                ? BoolType
                : AnyType;
        }

        switch (binary.Op)
        {
            case "и":
            case "или":
                EnsureBooleanCondition(left, binary.Left.Line, binary.Left.Col, context.Path);
                EnsureBooleanCondition(right, binary.Right.Line, binary.Right.Col, context.Path);
                return BoolType;

            case "+":
                if (IsAssignable(left, StringType) && IsAssignable(right, StringType))
                {
                    return StringType;
                }

                if (TryGetListElement(left, out var leftElement) && TryGetListElement(right, out var rightElement))
                {
                    return TypeRef.List(UnionOf([leftElement, rightElement]));
                }

                if (IsNumericType(left) && IsNumericType(right))
                {
                    return IsExactlyInt(left) && IsExactlyInt(right) ? IntType : FloatType;
                }

                break;

            case "-":
            case "*":
            case "/":
            case "%":
                if (IsNumericType(left) && IsNumericType(right))
                {
                    if (binary.Op == "/" && (!IsExactlyInt(left) || !IsExactlyInt(right)))
                    {
                        return FloatType;
                    }

                    return IsExactlyInt(left) && IsExactlyInt(right) ? IntType : FloatType;
                }

                break;

            case "==":
            case "!=":
                return BoolType;

            case "<":
            case "<=":
            case ">":
            case ">=":
                if ((IsNumericType(left) && IsNumericType(right)) ||
                    (IsAssignable(left, StringType) && IsAssignable(right, StringType)))
                {
                    return BoolType;
                }

                break;
        }

        throw YasnException.At(
            $"Оператор '{binary.Op}' не поддерживает типы '{FormatType(left)}' и '{FormatType(right)}'",
            binary.Line,
            binary.Col,
            context.Path);
    }

    private static TypeRef CheckCallExpr(CallExpr call, Scope scope, CheckContext context)
    {
        if (call.Callee is not IdentifierExpr callee)
        {
            throw YasnException.At("Вызов возможен только по имени функции", call.Line, call.Col, context.Path);
        }

        var argTypes = call.Args.Select(arg => CheckExpr(arg, scope, context)).ToList();

        if (TryCheckBuiltinCall(callee.Name, argTypes, call.Line, call.Col, context.Path, out var builtinReturn))
        {
            return builtinReturn;
        }

        if (!context.Signatures.TryGetValue(callee.Name, out var signature))
        {
            throw YasnException.At($"Неизвестная функция: {callee.Name}", call.Line, call.Col, context.Path);
        }

        if (argTypes.Count != signature.ParamTypes.Count)
        {
            throw YasnException.At(
                $"Функция '{callee.Name}' ожидает {signature.ParamTypes.Count} аргументов, получено {argTypes.Count}",
                call.Line,
                call.Col,
                context.Path);
        }

        for (var i = 0; i < argTypes.Count; i++)
        {
            var actual = argTypes[i];
            var expected = signature.ParamTypes[i];
            if (!IsAssignable(actual, expected))
            {
                throw YasnException.At(
                    $"Аргумент #{i + 1} функции '{callee.Name}' имеет тип '{FormatType(actual)}', ожидается '{FormatType(expected)}'",
                    call.Line,
                    call.Col,
                    context.Path);
            }
        }

        return signature.IsAsync ? TaskType : signature.ReturnType;
    }

    private static bool TryCheckBuiltinCall(
        string name,
        List<TypeRef> args,
        int line,
        int col,
        string? path,
        out TypeRef returnType)
    {
        returnType = AnyType;

        bool IsAnyOr(TypeRef actual, TypeRef expected) => IsAssignable(actual, expected);

        void RequireCount(int exact, string signature)
        {
            if (args.Count != exact)
            {
                throw YasnException.At($"{signature} принимает ровно {exact} аргумент(а)", line, col, path);
            }
        }

        void RequireCountRange(int min, int max, string signature)
        {
            if (args.Count < min || args.Count > max)
            {
                throw YasnException.At($"{signature} принимает от {min} до {max} аргументов", line, col, path);
            }
        }

        void RequireArg(int index, TypeRef expected, string message)
        {
            if (!IsAnyOr(args[index], expected))
            {
                throw YasnException.At(
                    $"{message}: получено '{FormatType(args[index])}', ожидалось '{FormatType(expected)}'",
                    line,
                    col,
                    path);
            }
        }

        switch (name)
        {
            case "печать":
                returnType = VoidType;
                return true;

            case "длина":
                RequireCount(1, "длина(x)");
                if (!IsAssignable(args[0], StringType) &&
                    !TryGetListElement(args[0], out _) &&
                    !TryGetDict(args[0], out _, out _) &&
                    !IsAnyType(args[0]))
                {
                    throw YasnException.At("длина(x) поддерживает только Строка/Список/Словарь", line, col, path);
                }

                returnType = IntType;
                return true;

            case "диапазон":
                RequireCount(2, "диапазон(нач, конец)");
                RequireArg(0, IntType, "диапазон(нач, конец)");
                RequireArg(1, IntType, "диапазон(нач, конец)");
                returnType = TypeRef.List(IntType);
                return true;

            case "ввод":
                RequireCount(0, "ввод()");
                returnType = StringType;
                return true;

            case "пауза":
                RequireCount(1, "пауза(мс)");
                RequireArg(0, IntType, "пауза(мс)");
                returnType = VoidType;
                return true;

            case "строка":
                RequireCount(1, "строка(x)");
                returnType = StringType;
                return true;

            case "число":
                RequireCount(1, "число(x)");
                returnType = IntType;
                return true;

            case "дробное":
                RequireCount(1, "дробное(x)");
                returnType = FloatType;
                return true;

            case "добавить":
                RequireCount(2, "добавить(список, элемент)");
                if (!TryGetListElement(args[0], out var elementType) && !IsAnyType(args[0]))
                {
                    throw YasnException.At("Первый аргумент добавить(...) должен быть Список", line, col, path);
                }

                if (!IsAnyType(args[0]) && !IsAssignable(args[1], elementType))
                {
                    throw YasnException.At(
                        $"Тип добавляемого элемента '{FormatType(args[1])}' не совместим с '{FormatType(elementType)}'",
                        line,
                        col,
                        path);
                }

                returnType = VoidType;
                return true;

            case "удалить":
                RequireCount(2, "удалить(список, индекс)");
                if (!TryGetListElement(args[0], out var removedType) && !IsAnyType(args[0]))
                {
                    throw YasnException.At("Первый аргумент удалить(...) должен быть Список", line, col, path);
                }

                RequireArg(1, IntType, "удалить(список, индекс)");
                returnType = IsAnyType(args[0]) ? AnyType : removedType;
                return true;

            case "ключи":
                RequireCount(1, "ключи(словарь)");
                if (!TryGetDict(args[0], out var keyType, out _) && !IsAnyType(args[0]))
                {
                    throw YasnException.At("Аргумент ключи(...) должен быть Словарь", line, col, path);
                }

                returnType = IsAnyType(args[0]) ? TypeRef.List(AnyType) : TypeRef.List(keyType);
                return true;

            case "содержит":
                RequireCount(2, "содержит(словарь, ключ)");
                if (!TryGetDict(args[0], out var dictKeyType, out _) && !IsAnyType(args[0]))
                {
                    throw YasnException.At("Первый аргумент содержит(...) должен быть Словарь", line, col, path);
                }

                if (!IsAnyType(args[0]) && !IsAssignable(args[1], dictKeyType))
                {
                    throw YasnException.At("Ключ не совместим с типом ключа словаря", line, col, path);
                }

                returnType = BoolType;
                return true;

            case "запустить":
                if (args.Count < 1)
                {
                    throw YasnException.At("запустить(имя, ...args) требует минимум 1 аргумент", line, col, path);
                }

                RequireArg(0, StringType, "запустить(имя, ...args)");
                returnType = TaskType;
                return true;

            case "готово":
                RequireCount(1, "готово(задача)");
                RequireArg(0, TaskType, "готово(задача)");
                returnType = BoolType;
                return true;

            case "ожидать":
                RequireCountRange(1, 2, "ожидать(задача[, таймаут_мс])");
                RequireArg(0, TaskType, "ожидать(задача[, таймаут_мс])");
                if (args.Count == 2)
                {
                    RequireArg(1, IntType, "ожидать(задача[, таймаут_мс])");
                }

                returnType = AnyType;
                return true;

            case "ожидать_все":
                RequireCountRange(1, 2, "ожидать_все(список_задач[, таймаут_мс])");
                if (TryGetListElement(args[0], out var taskElement))
                {
                    if (!IsAssignable(taskElement, TaskType))
                    {
                        throw YasnException.At("Список задач должен иметь элементы типа Задача", line, col, path);
                    }
                }
                else if (!IsAnyType(args[0]))
                {
                    throw YasnException.At("Первый аргумент ожидать_все(...) должен быть Список", line, col, path);
                }

                if (args.Count == 2)
                {
                    RequireArg(1, IntType, "ожидать_все(список_задач[, таймаут_мс])");
                }

                returnType = TypeRef.List(AnyType);
                return true;

            case "отменить":
                RequireCount(1, "отменить(задача)");
                RequireArg(0, TaskType, "отменить(задача)");
                returnType = BoolType;
                return true;

            case "файл_читать":
                RequireCount(1, "файл_читать(путь)");
                RequireArg(0, StringType, "файл_читать(путь)");
                returnType = StringType;
                return true;

            case "файл_записать":
                RequireCount(2, "файл_записать(путь, данные)");
                RequireArg(0, StringType, "файл_записать(путь, данные)");
                RequireArg(1, StringType, "файл_записать(путь, данные)");
                returnType = VoidType;
                return true;

            case "файл_существует":
                RequireCount(1, "файл_существует(путь)");
                RequireArg(0, StringType, "файл_существует(путь)");
                returnType = BoolType;
                return true;

            case "файл_удалить":
                RequireCount(1, "файл_удалить(путь)");
                RequireArg(0, StringType, "файл_удалить(путь)");
                returnType = VoidType;
                return true;

            case "json_разобрать":
                RequireCount(1, "json_разобрать(строка_json)");
                RequireArg(0, StringType, "json_разобрать(строка_json)");
                returnType = AnyType;
                return true;

            case "json_строка":
                RequireCount(1, "json_строка(значение)");
                returnType = StringType;
                return true;

            case "http_get":
                RequireCount(1, "http_get(url)");
                RequireArg(0, StringType, "http_get(url)");
                returnType = TypeRef.Dict(StringType, AnyType);
                return true;

            case "http_post":
                RequireCount(2, "http_post(url, body)");
                RequireArg(0, StringType, "http_post(url, body)");
                RequireArg(1, StringType, "http_post(url, body)");
                returnType = TypeRef.Dict(StringType, AnyType);
                return true;

            case "время_мс":
                RequireCount(0, "время_мс()");
                returnType = IntType;
                return true;

            case "случайное_цел":
                RequireCount(2, "случайное_цел(мин, макс)");
                RequireArg(0, IntType, "случайное_цел(мин, макс)");
                RequireArg(1, IntType, "случайное_цел(мин, макс)");
                returnType = IntType;
                return true;

            case "утверждать":
                RequireCountRange(1, 2, "утверждать(условие[, сообщение])");
                EnsureBooleanCondition(args[0], line, col, path);
                if (args.Count == 2)
                {
                    RequireArg(1, StringType, "утверждать(условие[, сообщение])");
                }

                returnType = VoidType;
                return true;

            case "утверждать_равно":
                RequireCountRange(2, 3, "утверждать_равно(факт, ожидание[, сообщение])");
                if (args.Count == 3)
                {
                    RequireArg(2, StringType, "утверждать_равно(факт, ожидание[, сообщение])");
                }

                returnType = VoidType;
                return true;

            case "провал":
                RequireCountRange(0, 1, "провал([сообщение])");
                if (args.Count == 1)
                {
                    RequireArg(0, StringType, "провал([сообщение])");
                }

                returnType = VoidType;
                return true;

            default:
                return false;
        }
    }

    private static void EnsureBooleanCondition(TypeRef type, int line, int col, string? path)
    {
        if (!IsAssignable(type, BoolType) && !IsAnyType(type))
        {
            throw YasnException.At($"Ожидался тип Лог, получен '{FormatType(type)}'", line, col, path);
        }
    }

    private static TypeRef ValidateIndexGet(TypeRef target, TypeRef index, int line, int col, string? path)
    {
        if (IsAnyType(target))
        {
            return AnyType;
        }

        if (TryGetListElement(target, out var element))
        {
            if (!IsAssignable(index, IntType))
            {
                throw YasnException.At("Индекс списка должен иметь тип Цел", line, col, path);
            }

            return element;
        }

        if (IsAssignable(target, StringType))
        {
            if (!IsAssignable(index, IntType))
            {
                throw YasnException.At("Индекс строки должен иметь тип Цел", line, col, path);
            }

            return StringType;
        }

        if (TryGetDict(target, out var keyType, out var valueType))
        {
            if (!IsAssignable(index, keyType))
            {
                throw YasnException.At(
                    $"Ключ словаря должен иметь тип '{FormatType(keyType)}', получен '{FormatType(index)}'",
                    line,
                    col,
                    path);
            }

            return valueType;
        }

        throw YasnException.At($"Индексация не поддерживается для типа '{FormatType(target)}'", line, col, path);
    }

    private static void ValidateIndexSet(TypeRef target, TypeRef index, TypeRef value, int line, int col, string? path)
    {
        if (IsAnyType(target))
        {
            return;
        }

        if (TryGetListElement(target, out var element))
        {
            if (!IsAssignable(index, IntType))
            {
                throw YasnException.At("Индекс списка должен иметь тип Цел", line, col, path);
            }

            if (!IsAssignable(value, element))
            {
                throw YasnException.At(
                    $"Нельзя присвоить '{FormatType(value)}' в элемент списка типа '{FormatType(element)}'",
                    line,
                    col,
                    path);
            }

            return;
        }

        if (TryGetDict(target, out var keyType, out var valueType))
        {
            if (!IsAssignable(index, keyType))
            {
                throw YasnException.At(
                    $"Ключ словаря должен иметь тип '{FormatType(keyType)}', получен '{FormatType(index)}'",
                    line,
                    col,
                    path);
            }

            if (!IsAssignable(value, valueType))
            {
                throw YasnException.At(
                    $"Нельзя присвоить '{FormatType(value)}' в значение словаря типа '{FormatType(valueType)}'",
                    line,
                    col,
                    path);
            }

            return;
        }

        throw YasnException.At($"Присваивание по индексу не поддерживается для '{FormatType(target)}'", line, col, path);
    }

    private static TypeRef GetIterableItemType(TypeRef type, int line, int col, string? path)
    {
        if (IsAnyType(type))
        {
            return AnyType;
        }

        if (TryGetListElement(type, out var element))
        {
            return element;
        }

        if (IsAssignable(type, StringType))
        {
            return StringType;
        }

        throw YasnException.At($"Оператор 'для' ожидает Список или Строка, получен '{FormatType(type)}'", line, col, path);
    }

    private static TypeRef InferLiteralType(LiteralExpr literal)
    {
        return literal.Kind switch
        {
            "int" => IntType,
            "float" => FloatType,
            "bool" => BoolType,
            "string" => StringType,
            "null" => NullType,
            _ => literal.Value switch
            {
                null => NullType,
                bool => BoolType,
                long or int => IntType,
                double or float or decimal => FloatType,
                string => StringType,
                _ => AnyType,
            },
        };
    }

    private static TypeRef FromTypeNode(TypeNode node, string? path)
    {
        return node switch
        {
            PrimitiveTypeNode primitive when PrimitiveNames.Contains(primitive.Name) => TypeRef.Primitive(primitive.Name),
            PrimitiveTypeNode primitive => throw YasnException.At($"Неизвестный тип: {primitive.Name}", primitive.Line, primitive.Col, path),
            ListTypeNode list => TypeRef.List(FromTypeNode(list.Element, path)),
            DictTypeNode dict => TypeRef.Dict(FromTypeNode(dict.Key, path), FromTypeNode(dict.Value, path)),
            UnionTypeNode union => UnionOf(union.Variants.Select(variant => FromTypeNode(variant, path))),
            _ => throw YasnException.At("Неизвестный тип узла type annotation", node.Line, node.Col, path),
        };
    }

    private static bool IsAnyType(TypeRef type)
    {
        return TypeEquals(type, AnyType);
    }

    private static bool IsAssignable(TypeRef actual, TypeRef expected)
    {
        if (TypeEquals(expected, AnyType) || TypeEquals(actual, AnyType))
        {
            return true;
        }

        if (TypeEquals(actual, expected))
        {
            return true;
        }

        if (expected.Kind == TypeKind.Union)
        {
            return expected.Variants.Any(variant => IsAssignable(actual, variant));
        }

        if (actual.Kind == TypeKind.Union)
        {
            return actual.Variants.All(variant => IsAssignable(variant, expected));
        }

        if (expected.Kind == TypeKind.List && actual.Kind == TypeKind.List)
        {
            return IsAssignable(actual.ElementType!, expected.ElementType!);
        }

        if (expected.Kind == TypeKind.Dict && actual.Kind == TypeKind.Dict)
        {
            return IsAssignable(actual.KeyType!, expected.KeyType!) &&
                   IsAssignable(actual.ValueType!, expected.ValueType!);
        }

        return false;
    }

    private static bool IsNumericType(TypeRef type)
    {
        if (TypeEquals(type, AnyType))
        {
            return true;
        }

        if (TypeEquals(type, IntType) || TypeEquals(type, FloatType))
        {
            return true;
        }

        if (type.Kind == TypeKind.Union)
        {
            return type.Variants.All(IsNumericType);
        }

        return false;
    }

    private static bool IsExactlyInt(TypeRef type)
    {
        return TypeEquals(type, IntType);
    }

    private static bool TryGetListElement(TypeRef type, out TypeRef element)
    {
        if (type.Kind == TypeKind.List)
        {
            element = type.ElementType!;
            return true;
        }

        if (type.Kind == TypeKind.Union)
        {
            var listVariants = type.Variants.Where(variant => variant.Kind == TypeKind.List).ToList();
            if (listVariants.Count == 0)
            {
                element = AnyType;
                return false;
            }

            element = UnionOf(listVariants.Select(variant => variant.ElementType!));
            return true;
        }

        element = AnyType;
        return false;
    }

    private static bool TryGetDict(TypeRef type, out TypeRef key, out TypeRef value)
    {
        if (type.Kind == TypeKind.Dict)
        {
            key = type.KeyType!;
            value = type.ValueType!;
            return true;
        }

        if (type.Kind == TypeKind.Union)
        {
            var dictVariants = type.Variants.Where(variant => variant.Kind == TypeKind.Dict).ToList();
            if (dictVariants.Count == 0)
            {
                key = AnyType;
                value = AnyType;
                return false;
            }

            key = UnionOf(dictVariants.Select(variant => variant.KeyType!));
            value = UnionOf(dictVariants.Select(variant => variant.ValueType!));
            return true;
        }

        key = AnyType;
        value = AnyType;
        return false;
    }

    private static TypeRef UnionOf(IEnumerable<TypeRef> types)
    {
        var flattened = new List<TypeRef>();
        foreach (var type in types)
        {
            if (type.Kind == TypeKind.Union)
            {
                flattened.AddRange(type.Variants);
            }
            else
            {
                flattened.Add(type);
            }
        }

        var unique = new List<TypeRef>();
        foreach (var type in flattened)
        {
            if (TypeEquals(type, AnyType))
            {
                return AnyType;
            }

            if (!unique.Any(existing => TypeEquals(existing, type)))
            {
                unique.Add(type);
            }
        }

        if (unique.Count == 0)
        {
            return AnyType;
        }

        if (unique.Count == 1)
        {
            return unique[0];
        }

        return TypeRef.Union(unique);
    }

    private static bool TypeEquals(TypeRef left, TypeRef right)
    {
        if (left.Kind != right.Kind)
        {
            return false;
        }

        return left.Kind switch
        {
            TypeKind.Primitive => string.Equals(left.Name, right.Name, StringComparison.Ordinal),
            TypeKind.List => TypeEquals(left.ElementType!, right.ElementType!),
            TypeKind.Dict => TypeEquals(left.KeyType!, right.KeyType!) && TypeEquals(left.ValueType!, right.ValueType!),
            TypeKind.Union => left.Variants.Count == right.Variants.Count &&
                              left.Variants.All(variant => right.Variants.Any(other => TypeEquals(variant, other))),
            _ => false,
        };
    }

    private static string FormatType(TypeRef type)
    {
        return type.Kind switch
        {
            TypeKind.Primitive => type.Name!,
            TypeKind.List => $"Список[{FormatType(type.ElementType!)}]",
            TypeKind.Dict => $"Словарь[{FormatType(type.KeyType!)}, {FormatType(type.ValueType!)}]",
            TypeKind.Union => string.Join(" | ", type.Variants.Select(FormatType)),
            _ => "Любой",
        };
    }

    private sealed class CheckContext
    {
        public CheckContext(string? path, Dictionary<string, FuncSignature> signatures)
        {
            Path = path;
            Signatures = signatures;
        }

        public string? Path { get; }

        public Dictionary<string, FuncSignature> Signatures { get; }
    }

    private sealed class Scope
    {
        private readonly Scope? _parent;
        private readonly Dictionary<string, TypeRef> _values;

        private Scope(Scope? parent, Dictionary<string, TypeRef>? values = null)
        {
            _parent = parent;
            _values = values ?? new Dictionary<string, TypeRef>(StringComparer.Ordinal);
        }

        public static Scope CreateRoot(Dictionary<string, TypeRef> values)
        {
            return new Scope(parent: null, values: new Dictionary<string, TypeRef>(values, StringComparer.Ordinal));
        }

        public static Scope CreateChild(Scope parent)
        {
            return new Scope(parent);
        }

        public void Declare(string name, TypeRef type, int line, int col, string? path)
        {
            if (_values.ContainsKey(name))
            {
                throw YasnException.At($"Символ '{name}' уже объявлен в текущей области", line, col, path);
            }

            _values[name] = type;
        }

        public void AssignOrDeclare(string name, TypeRef type, int line, int col, string? path)
        {
            if (_values.ContainsKey(name))
            {
                _values[name] = type;
                return;
            }

            if (_parent is not null && _parent.TryResolve(name, out _))
            {
                _parent.Assign(name, type, line, col, path);
                return;
            }

            _values[name] = type;
        }

        public void Assign(string name, TypeRef type, int line, int col, string? path)
        {
            if (_values.ContainsKey(name))
            {
                _values[name] = type;
                return;
            }

            if (_parent is not null)
            {
                _parent.Assign(name, type, line, col, path);
                return;
            }

            throw YasnException.At($"Неизвестная переменная: {name}", line, col, path);
        }

        public TypeRef Resolve(string name, int line, int col, string? path)
        {
            if (TryResolve(name, out var type))
            {
                return type;
            }

            throw YasnException.At($"Неизвестная переменная: {name}", line, col, path);
        }

        public bool TryResolve(string name, out TypeRef type)
        {
            if (_values.TryGetValue(name, out type!))
            {
                return true;
            }

            if (_parent is not null)
            {
                return _parent.TryResolve(name, out type!);
            }

            type = AnyType;
            return false;
        }
    }

    private sealed record FuncSignature(string Name, List<TypeRef> ParamTypes, TypeRef ReturnType, bool IsAsync);

    private enum TypeKind
    {
        Primitive,
        List,
        Dict,
        Union,
    }

    private sealed class TypeRef
    {
        private TypeRef(TypeKind kind)
        {
            Kind = kind;
        }

        public TypeKind Kind { get; }

        public string? Name { get; private init; }

        public TypeRef? ElementType { get; private init; }

        public TypeRef? KeyType { get; private init; }

        public TypeRef? ValueType { get; private init; }

        public IReadOnlyList<TypeRef> Variants { get; private init; } = [];

        public static TypeRef Primitive(string name)
        {
            return new TypeRef(TypeKind.Primitive) { Name = name };
        }

        public static TypeRef List(TypeRef element)
        {
            return new TypeRef(TypeKind.List) { ElementType = element };
        }

        public static TypeRef Dict(TypeRef key, TypeRef value)
        {
            return new TypeRef(TypeKind.Dict) { KeyType = key, ValueType = value };
        }

        public static TypeRef Union(IReadOnlyList<TypeRef> variants)
        {
            return new TypeRef(TypeKind.Union) { Variants = variants };
        }
    }
}
