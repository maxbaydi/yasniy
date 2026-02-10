using YasnNative.Bytecode;

namespace YasnNative.Core;

public sealed class Compiler
{
    private readonly string? _path;

    public Compiler(string? path = null)
    {
        _path = path;
    }

    public ProgramBC Compile(ProgramNode program)
    {
        var globalSlots = CollectGlobalSlots(program);
        var asyncFunctions = program.Statements
            .OfType<FuncDeclStmt>()
            .Where(stmt => stmt.IsAsync)
            .Select(stmt => stmt.Name)
            .ToHashSet(StringComparer.Ordinal);

        var functions = new Dictionary<string, FunctionBC>(StringComparer.Ordinal);
        foreach (var stmt in program.Statements)
        {
            if (stmt is not FuncDeclStmt fnStmt)
            {
                continue;
            }

            var fnCompiler = new FunctionCompiler(
                _path,
                fnStmt.Name,
                [.. fnStmt.Params.Select(p => p.Name)],
                globalSlots,
                isEntry: false,
                asyncFunctions);

            foreach (var bodyStmt in fnStmt.Body)
            {
                fnCompiler.CompileStmt(bodyStmt);
            }

            if (!fnCompiler.EndsWithTerminal())
            {
                fnCompiler.Emit("CONST_NULL");
                fnCompiler.Emit("RET");
            }

            functions[fnStmt.Name] = fnCompiler.Finish();
        }

        var entryCompiler = new FunctionCompiler(
            _path,
            "__entry__",
            [],
            globalSlots,
            isEntry: true,
            asyncFunctions);

        foreach (var stmt in program.Statements)
        {
            if (stmt is FuncDeclStmt)
            {
                continue;
            }

            entryCompiler.CompileStmt(stmt);
        }

        if (functions.ContainsKey("main"))
        {
            entryCompiler.Emit("CALL", "main", 0L);
            if (asyncFunctions.Contains("main"))
            {
                entryCompiler.Emit("CALL", "ожидать", 1L);
            }

            entryCompiler.Emit("POP");
        }

        entryCompiler.Emit("HALT");

        return new ProgramBC
        {
            Functions = functions,
            Entry = entryCompiler.Finish(),
            GlobalCount = globalSlots.Count,
        };
    }

    private static Dictionary<string, int> CollectGlobalSlots(ProgramNode program)
    {
        var slots = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var stmt in program.Statements)
        {
            if (stmt is VarDeclStmt varDecl && !slots.ContainsKey(varDecl.Name))
            {
                slots[varDecl.Name] = slots.Count;
            }
        }

        return slots;
    }

    private sealed class LoopContext
    {
        public List<int> BreakJumps { get; } = [];

        public List<int> ContinueJumps { get; } = [];
    }

    private sealed class FunctionCompiler
    {
        private readonly string? _path;
        private readonly string _name;
        private readonly List<string> _params;
        private readonly Dictionary<string, int> _globalSlots;
        private readonly bool _isEntry;
        private readonly HashSet<string> _asyncFunctions;
        private readonly List<InstructionBC> _instructions = [];
        private readonly List<Dictionary<string, int>> _scopes = [];
        private readonly List<LoopContext> _loopStack = [];
        private int _nextSlot;

        public FunctionCompiler(
            string? path,
            string name,
            List<string> parameters,
            Dictionary<string, int> globalSlots,
            bool isEntry,
            HashSet<string> asyncFunctions)
        {
            _path = path;
            _name = name;
            _params = parameters;
            _globalSlots = globalSlots;
            _isEntry = isEntry;
            _asyncFunctions = asyncFunctions;

            PushScope();
            foreach (var param in parameters)
            {
                DefineVar(param, 1, 1);
            }
        }

        public FunctionBC Finish()
        {
            return new FunctionBC
            {
                Name = _name,
                Params = _params,
                LocalCount = _nextSlot,
                Instructions = _instructions,
            };
        }

        public bool EndsWithTerminal()
        {
            return _instructions.Count > 0 && (_instructions[^1].Op is "RET" or "HALT");
        }

        public int Emit(string op, params object?[] args)
        {
            _instructions.Add(new InstructionBC { Op = op, Args = [.. args] });
            return _instructions.Count - 1;
        }

        private void Patch(int index, int value)
        {
            _instructions[index].Args[0] = (long)value;
        }

        private int CurrentIp()
        {
            return _instructions.Count;
        }

        private void PushScope()
        {
            _scopes.Add(new Dictionary<string, int>(StringComparer.Ordinal));
        }

        private void PopScope()
        {
            _scopes.RemoveAt(_scopes.Count - 1);
        }

        private int DefineVar(string name, int line, int col)
        {
            var current = _scopes[^1];
            if (current.ContainsKey(name))
            {
                throw YasnException.At($"Переменная '{name}' уже объявлена в блоке", line, col, _path);
            }

            var slot = AllocateTemp();
            current[name] = slot;
            return slot;
        }

        private int AllocateTemp()
        {
            var slot = _nextSlot;
            _nextSlot++;
            return slot;
        }

        private int? ResolveLocalVar(string name)
        {
            for (var i = _scopes.Count - 1; i >= 0; i--)
            {
                if (_scopes[i].TryGetValue(name, out var slot))
                {
                    return slot;
                }
            }

            return null;
        }

        private (string Place, int Slot) ResolveVar(string name, int line, int col)
        {
            var local = ResolveLocalVar(name);
            if (local is int localSlot)
            {
                return ("local", localSlot);
            }

            if (_globalSlots.TryGetValue(name, out var globalSlot))
            {
                return ("global", globalSlot);
            }

            throw YasnException.At($"Неизвестная переменная: {name}", line, col, _path);
        }

        public void CompileStmt(Stmt stmt)
        {
            switch (stmt)
            {
                case ImportAllStmt:
                case ImportFromStmt:
                    throw YasnException.At(
                        "Операторы подключения пока не поддерживаются в нативном компиляторе",
                        stmt.Line,
                        stmt.Col,
                        _path);

                case VarDeclStmt varDecl:
                    CompileExpr(varDecl.Value);
                    if (_isEntry && _scopes.Count == 1 && _globalSlots.TryGetValue(varDecl.Name, out var globalSlot))
                    {
                        Emit("GSTORE", (long)globalSlot);
                    }
                    else
                    {
                        var slot = DefineVar(varDecl.Name, varDecl.Line, varDecl.Col);
                        Emit("STORE", (long)slot);
                    }

                    return;

                case AssignStmt assign:
                {
                    CompileExpr(assign.Value);
                    var place = ResolveVar(assign.Name, assign.Line, assign.Col);
                    Emit(place.Place == "local" ? "STORE" : "GSTORE", (long)place.Slot);
                    return;
                }

                case IndexAssignStmt indexAssign:
                    CompileExpr(indexAssign.Target);
                    CompileExpr(indexAssign.Index);
                    CompileExpr(indexAssign.Value);
                    Emit("INDEX_SET");
                    Emit("POP");
                    return;

                case ExprStmt exprStmt:
                    CompileExpr(exprStmt.Expr);
                    Emit("POP");
                    return;

                case ReturnStmt ret:
                    CompileExpr(ret.Value);
                    Emit("RET");
                    return;

                case BreakStmt:
                    if (_loopStack.Count == 0)
                    {
                        throw YasnException.At("'прервать' допустим только внутри цикла", stmt.Line, stmt.Col, _path);
                    }

                    var breakJmp = Emit("JMP", -1L);
                    _loopStack[^1].BreakJumps.Add(breakJmp);
                    return;

                case ContinueStmt:
                    if (_loopStack.Count == 0)
                    {
                        throw YasnException.At("'продолжить' допустим только внутри цикла", stmt.Line, stmt.Col, _path);
                    }

                    var continueJmp = Emit("JMP", -1L);
                    _loopStack[^1].ContinueJumps.Add(continueJmp);
                    return;

                case IfStmt ifStmt:
                {
                    CompileExpr(ifStmt.Condition);
                    var jumpFalse = Emit("JMP_FALSE", -1L);

                    PushScope();
                    foreach (var inner in ifStmt.ThenBody)
                    {
                        CompileStmt(inner);
                    }

                    PopScope();

                    if (ifStmt.ElseBody is not null)
                    {
                        var jumpEnd = Emit("JMP", -1L);
                        Patch(jumpFalse, CurrentIp());
                        PushScope();
                        foreach (var inner in ifStmt.ElseBody)
                        {
                            CompileStmt(inner);
                        }

                        PopScope();
                        Patch(jumpEnd, CurrentIp());
                    }
                    else
                    {
                        Patch(jumpFalse, CurrentIp());
                    }

                    return;
                }

                case WhileStmt whileStmt:
                {
                    var loopStart = CurrentIp();
                    CompileExpr(whileStmt.Condition);
                    var jumpEnd = Emit("JMP_FALSE", -1L);

                    _loopStack.Add(new LoopContext());
                    PushScope();
                    foreach (var inner in whileStmt.Body)
                    {
                        CompileStmt(inner);
                    }

                    PopScope();
                    var ctx = _loopStack[^1];
                    _loopStack.RemoveAt(_loopStack.Count - 1);

                    foreach (var jump in ctx.ContinueJumps)
                    {
                        Patch(jump, loopStart);
                    }

                    Emit("JMP", (long)loopStart);
                    var endIp = CurrentIp();
                    Patch(jumpEnd, endIp);
                    foreach (var jump in ctx.BreakJumps)
                    {
                        Patch(jump, endIp);
                    }

                    return;
                }

                case ForStmt forStmt:
                {
                    PushScope();
                    var iterSlot = AllocateTemp();
                    var idxSlot = AllocateTemp();
                    var lenSlot = AllocateTemp();
                    var loopVarSlot = DefineVar(forStmt.VarName, forStmt.Line, forStmt.Col);

                    CompileExpr(forStmt.Iterable);
                    Emit("STORE", (long)iterSlot);
                    Emit("CONST", 0L);
                    Emit("STORE", (long)idxSlot);
                    Emit("LOAD", (long)iterSlot);
                    Emit("LEN");
                    Emit("STORE", (long)lenSlot);

                    var loopStart = CurrentIp();
                    Emit("LOAD", (long)idxSlot);
                    Emit("LOAD", (long)lenSlot);
                    Emit("LT");
                    var jumpEnd = Emit("JMP_FALSE", -1L);

                    Emit("LOAD", (long)iterSlot);
                    Emit("LOAD", (long)idxSlot);
                    Emit("INDEX_GET");
                    Emit("STORE", (long)loopVarSlot);

                    _loopStack.Add(new LoopContext());
                    foreach (var inner in forStmt.Body)
                    {
                        CompileStmt(inner);
                    }

                    var ctx = _loopStack[^1];
                    _loopStack.RemoveAt(_loopStack.Count - 1);

                    var incrementStart = CurrentIp();
                    foreach (var jump in ctx.ContinueJumps)
                    {
                        Patch(jump, incrementStart);
                    }

                    Emit("LOAD", (long)idxSlot);
                    Emit("CONST", 1L);
                    Emit("ADD");
                    Emit("STORE", (long)idxSlot);
                    Emit("JMP", (long)loopStart);

                    var endIp = CurrentIp();
                    Patch(jumpEnd, endIp);
                    foreach (var jump in ctx.BreakJumps)
                    {
                        Patch(jump, endIp);
                    }

                    PopScope();
                    return;
                }

                case FuncDeclStmt fn:
                    throw YasnException.At("Вложенные функции не поддерживаются", fn.Line, fn.Col, _path);

                default:
                    throw YasnException.At("Неизвестный тип оператора для компиляции", stmt.Line, stmt.Col, _path);
            }
        }

        private void CompileExpr(Expr expr)
        {
            switch (expr)
            {
                case LiteralExpr literal:
                    Emit("CONST", literal.Value);
                    return;

                case IdentifierExpr ident:
                {
                    var place = ResolveVar(ident.Name, ident.Line, ident.Col);
                    Emit(place.Place == "local" ? "LOAD" : "GLOAD", (long)place.Slot);
                    return;
                }

                case MemberExpr member:
                    throw YasnException.At(
                        "Оператор '.' пока не поддерживается в нативном компиляторе",
                        member.Line,
                        member.Col,
                        _path);

                case ListLiteralExpr list:
                    foreach (var item in list.Elements)
                    {
                        CompileExpr(item);
                    }

                    Emit("MAKE_LIST", (long)list.Elements.Count);
                    return;

                case DictLiteralExpr dict:
                    foreach (var (key, value) in dict.Entries)
                    {
                        CompileExpr(key);
                        CompileExpr(value);
                    }

                    Emit("MAKE_DICT", (long)dict.Entries.Count);
                    return;

                case IndexExpr indexExpr:
                    CompileExpr(indexExpr.Target);
                    CompileExpr(indexExpr.Index);
                    Emit("INDEX_GET");
                    return;

                case UnaryExpr unary:
                    CompileExpr(unary.Operand);
                    if (unary.Op == "не")
                    {
                        Emit("NOT");
                        return;
                    }

                    if (unary.Op == "-")
                    {
                        Emit("NEG");
                        return;
                    }

                    throw YasnException.At($"Неизвестный унарный оператор: {unary.Op}", unary.Line, unary.Col, _path);

                case AwaitExpr awaitExpr:
                    CompileExpr(awaitExpr.Operand);
                    Emit("CALL", "ожидать", 1L);
                    return;

                case BinaryExpr binary:
                    if (binary.Op == "и")
                    {
                        CompileShortCircuitAnd(binary);
                        return;
                    }

                    if (binary.Op == "или")
                    {
                        CompileShortCircuitOr(binary);
                        return;
                    }

                    CompileExpr(binary.Left);
                    CompileExpr(binary.Right);

                    var mappedOp = binary.Op switch
                    {
                        "+" => "ADD",
                        "-" => "SUB",
                        "*" => "MUL",
                        "/" => "DIV",
                        "%" => "MOD",
                        "==" => "EQ",
                        "!=" => "NE",
                        "<" => "LT",
                        "<=" => "LE",
                        ">" => "GT",
                        ">=" => "GE",
                        _ => null,
                    };

                    if (mappedOp is null)
                    {
                        throw YasnException.At($"Неизвестный бинарный оператор: {binary.Op}", binary.Line, binary.Col, _path);
                    }

                    Emit(mappedOp);
                    return;

                case CallExpr call:
                    if (call.Callee is not IdentifierExpr calleeIdent)
                    {
                        throw YasnException.At("Вызов возможен только по имени функции", call.Line, call.Col, _path);
                    }

                    if (_asyncFunctions.Contains(calleeIdent.Name))
                    {
                        Emit("CONST", calleeIdent.Name);
                        foreach (var arg in call.Args)
                        {
                            CompileExpr(arg);
                        }

                        Emit("CALL", "запустить", (long)(call.Args.Count + 1));
                        return;
                    }

                    foreach (var arg in call.Args)
                    {
                        CompileExpr(arg);
                    }

                    Emit("CALL", calleeIdent.Name, (long)call.Args.Count);
                    return;

                default:
                    throw YasnException.At("Неизвестный тип выражения для компиляции", expr.Line, expr.Col, _path);
            }
        }

        private void CompileShortCircuitAnd(BinaryExpr expr)
        {
            CompileExpr(expr.Left);
            var leftFalse = Emit("JMP_FALSE", -1L);
            CompileExpr(expr.Right);
            var rightFalse = Emit("JMP_FALSE", -1L);
            Emit("CONST", true);
            var jumpEnd = Emit("JMP", -1L);
            var falseLabel = CurrentIp();
            Emit("CONST", false);
            var endLabel = CurrentIp();
            Patch(leftFalse, falseLabel);
            Patch(rightFalse, falseLabel);
            Patch(jumpEnd, endLabel);
        }

        private void CompileShortCircuitOr(BinaryExpr expr)
        {
            CompileExpr(expr.Left);
            var checkRight = Emit("JMP_FALSE", -1L);
            Emit("CONST", true);
            var jumpEndLeftTrue = Emit("JMP", -1L);

            var rightLabel = CurrentIp();
            Patch(checkRight, rightLabel);
            CompileExpr(expr.Right);
            var rightFalse = Emit("JMP_FALSE", -1L);
            Emit("CONST", true);
            var jumpEndRightTrue = Emit("JMP", -1L);

            var falseLabel = CurrentIp();
            Emit("CONST", false);
            var endLabel = CurrentIp();

            Patch(rightFalse, falseLabel);
            Patch(jumpEndLeftTrue, endLabel);
            Patch(jumpEndRightTrue, endLabel);
        }
    }
}
