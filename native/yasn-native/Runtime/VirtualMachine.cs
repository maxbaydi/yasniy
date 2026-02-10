using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using YasnNative.Bytecode;
using YasnNative.Core;

namespace YasnNative.Runtime;

public sealed class TaskHandle
{
    public TaskHandle(int taskId, Task<object?> future, CancellationTokenSource cancellation)
    {
        TaskId = taskId;
        Future = future;
        Cancellation = cancellation;
    }

    public int TaskId { get; }

    public Task<object?> Future { get; }

    public CancellationTokenSource Cancellation { get; }
}

public sealed class VirtualMachine : IDisposable
{
    private delegate object? BuiltinFn(List<object?> args, object?[] globalsStore);

    private readonly ProgramBC _program;
    private readonly string? _path;
    private readonly Dictionary<string, BuiltinFn> _builtins;
    private readonly object _taskLock = new();
    private readonly List<TaskHandle> _activeTasks = [];
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };
    private object?[] _globals = [];
    private bool _initialized;
    private int _taskCounter;

    public VirtualMachine(ProgramBC program, string? path = null)
    {
        _program = program;
        _path = path;

        _builtins = new Dictionary<string, BuiltinFn>(StringComparer.Ordinal)
        {
            ["печать"] = BuiltinPrint,
            ["длина"] = BuiltinLen,
            ["диапазон"] = BuiltinRange,
            ["ввод"] = BuiltinInput,
            ["пауза"] = BuiltinSleep,
            ["строка"] = BuiltinToString,
            ["число"] = BuiltinToInt,
            ["добавить"] = BuiltinAppend,
            ["удалить"] = BuiltinRemove,
            ["ключи"] = BuiltinKeys,
            ["содержит"] = BuiltinContains,
            ["файл_читать"] = BuiltinFileRead,
            ["файл_записать"] = BuiltinFileWrite,
            ["файл_существует"] = BuiltinFileExists,
            ["файл_удалить"] = BuiltinFileDelete,
            ["json_разобрать"] = BuiltinJsonParse,
            ["json_строка"] = BuiltinJsonStringify,
            ["http_get"] = BuiltinHttpGet,
            ["http_post"] = BuiltinHttpPost,
            ["время_мс"] = BuiltinNowMs,
            ["случайное_цел"] = BuiltinRandomInt,
            ["утверждать"] = BuiltinAssert,
            ["утверждать_равно"] = BuiltinAssertEqual,
            ["провал"] = BuiltinFail,
            ["запустить"] = BuiltinSpawn,
            ["готово"] = BuiltinDone,
            ["ожидать"] = BuiltinWait,
            ["ожидать_все"] = BuiltinWaitAll,
            ["отменить"] = BuiltinCancel,
        };
    }

    public ProgramBC Program => _program;

    public void Run()
    {
        _globals = new object?[_program.GlobalCount];
        ExecuteFunction(_program.Entry, [], _globals);
        _initialized = true;
    }

    public object? CallFunction(string functionName, List<object?>? args = null, bool resetState = true)
    {
        var callArgs = args is null ? new List<object?>() : new List<object?>(args);
        if (resetState || !_initialized)
        {
            Run();
        }

        return CallFunctionInternal(functionName, callArgs, _globals);
    }

    public object? InvokeHostFunction(string functionName, List<object?>? args = null, object?[]? globalsStore = null)
    {
        var callArgs = args is null ? new List<object?>() : new List<object?>(args);
        if (globalsStore is null)
        {
            if (!_initialized)
            {
                Run();
            }

            globalsStore = _globals;
        }

        return CallFunctionInternal(functionName, callArgs, globalsStore);
    }

    private object? CallFunctionInternal(string functionName, List<object?> args, object?[] globalsStore)
    {
        if (_builtins.TryGetValue(functionName, out var builtin))
        {
            return builtin(args, globalsStore);
        }

        if (!_program.Functions.TryGetValue(functionName, out var fn))
        {
            throw new YasnException($"Неизвестная функция: {functionName}", path: _path);
        }

        return ExecuteFunction(fn, args, globalsStore);
    }

    private object? ExecuteFunction(FunctionBC fn, List<object?> args, object?[] globalsStore)
    {
        if (args.Count != fn.Params.Count)
        {
            throw new YasnException(
                $"Функция '{fn.Name}' ожидает {fn.Params.Count} аргументов, получено {args.Count}",
                path: _path);
        }

        var locals = new object?[fn.LocalCount];
        for (var i = 0; i < args.Count; i++)
        {
            locals[i] = args[i];
        }

        var stack = new List<object?>();
        var ip = 0;

        while (ip < fn.Instructions.Count)
        {
            var ins = fn.Instructions[ip];
            ip++;

            switch (ins.Op)
            {
                case "CONST":
                    stack.Add(ins.Args.Count > 0 ? ins.Args[0] : null);
                    break;
                case "CONST_NULL":
                    stack.Add(null);
                    break;
                case "LOAD":
                    stack.Add(locals[ToInt(ins.Args[0])]);
                    break;
                case "STORE":
                    locals[ToInt(ins.Args[0])] = Pop(stack);
                    break;
                case "GLOAD":
                    stack.Add(globalsStore[ToInt(ins.Args[0])]);
                    break;
                case "GSTORE":
                    globalsStore[ToInt(ins.Args[0])] = Pop(stack);
                    break;
                case "POP":
                    if (stack.Count > 0)
                    {
                        stack.RemoveAt(stack.Count - 1);
                    }
                    break;
                case "ADD":
                {
                    var (b, a) = Pop2(stack);
                    stack.Add(AddValues(a, b));
                    break;
                }
                case "SUB":
                {
                    var (b, a) = Pop2(stack);
                    stack.Add(SubValues(a, b));
                    break;
                }
                case "MUL":
                {
                    var (b, a) = Pop2(stack);
                    stack.Add(MulValues(a, b));
                    break;
                }
                case "DIV":
                {
                    var (b, a) = Pop2(stack);
                    stack.Add(DivValues(a, b));
                    break;
                }
                case "MOD":
                {
                    var (b, a) = Pop2(stack);
                    stack.Add(ModValues(a, b));
                    break;
                }
                case "NEG":
                    stack.Add(NegValue(Pop(stack)));
                    break;
                case "NOT":
                    stack.Add(!IsTruthy(Pop(stack)));
                    break;
                case "AND":
                {
                    var (b, a) = Pop2(stack);
                    stack.Add(IsTruthy(a) && IsTruthy(b));
                    break;
                }
                case "OR":
                {
                    var (b, a) = Pop2(stack);
                    stack.Add(IsTruthy(a) || IsTruthy(b));
                    break;
                }
                case "EQ":
                {
                    var (b, a) = Pop2(stack);
                    stack.Add(EqualsValue(a, b));
                    break;
                }
                case "NE":
                {
                    var (b, a) = Pop2(stack);
                    stack.Add(!EqualsValue(a, b));
                    break;
                }
                case "LT":
                {
                    var (b, a) = Pop2(stack);
                    stack.Add(CompareValues(a, b) < 0);
                    break;
                }
                case "LE":
                {
                    var (b, a) = Pop2(stack);
                    stack.Add(CompareValues(a, b) <= 0);
                    break;
                }
                case "GT":
                {
                    var (b, a) = Pop2(stack);
                    stack.Add(CompareValues(a, b) > 0);
                    break;
                }
                case "GE":
                {
                    var (b, a) = Pop2(stack);
                    stack.Add(CompareValues(a, b) >= 0);
                    break;
                }
                case "JMP":
                    ip = ToInt(ins.Args[0]);
                    break;
                case "JMP_FALSE":
                {
                    var cond = Pop(stack);
                    if (!IsTruthy(cond))
                    {
                        ip = ToInt(ins.Args[0]);
                    }
                    break;
                }
                case "CALL":
                {
                    var fnName = Convert.ToString(ins.Args[0], CultureInfo.InvariantCulture) ?? string.Empty;
                    var argc = ToInt(ins.Args[1]);
                    var callArgs = new List<object?>(argc);
                    for (var i = 0; i < argc; i++)
                    {
                        callArgs.Add(Pop(stack));
                    }
                    callArgs.Reverse();
                    var result = CallFunctionInternal(fnName, callArgs, globalsStore);
                    stack.Add(result);
                    break;
                }
                case "RET":
                    return stack.Count > 0 ? Pop(stack) : null;
                case "MAKE_LIST":
                {
                    var count = ToInt(ins.Args[0]);
                    var items = new List<object?>(count);
                    for (var i = 0; i < count; i++)
                    {
                        items.Add(Pop(stack));
                    }
                    items.Reverse();
                    stack.Add(items);
                    break;
                }
                case "MAKE_DICT":
                {
                    var count = ToInt(ins.Args[0]);
                    var raw = new List<object?>(count * 2);
                    for (var i = 0; i < count * 2; i++)
                    {
                        raw.Add(Pop(stack));
                    }
                    raw.Reverse();
                    var dict = new Dictionary<object, object?>(RuntimeValueComparer.Instance);
                    for (var i = 0; i < raw.Count; i += 2)
                    {
                        var key = EnsureDictionaryKey(raw[i], "создание словаря");
                        dict[key] = raw[i + 1];
                    }
                    stack.Add(dict);
                    break;
                }
                case "INDEX_GET":
                {
                    var idx = Pop(stack);
                    var target = Pop(stack);
                    stack.Add(IndexGet(target, idx));
                    break;
                }
                case "INDEX_SET":
                {
                    var value = Pop(stack);
                    var idx = Pop(stack);
                    var target = Pop(stack);
                    IndexSet(target, idx, value);
                    stack.Add(value);
                    break;
                }
                case "LEN":
                    stack.Add(GetLength(Pop(stack)));
                    break;
                case "HALT":
                    return null;
                default:
                    throw new YasnException($"Неизвестная инструкция VM: {ins.Op}", path: _path);
            }
        }

        return null;
    }

    private static object? Pop(List<object?> stack)
    {
        if (stack.Count == 0)
        {
            return null;
        }

        var value = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return value;
    }

    private static (object? B, object? A) Pop2(List<object?> stack)
    {
        var b = Pop(stack);
        var a = Pop(stack);
        return (b, a);
    }

    private static bool IsNumeric(object? value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;
    }

    private static bool IsIntegral(object? value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long;
    }

    private static double ToDouble(object? value)
    {
        return value switch
        {
            sbyte v => v,
            byte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => v,
            long v => v,
            float v => v,
            double v => v,
            decimal v => (double)v,
            _ => throw new YasnException("Ожидалось числовое значение"),
        };
    }

    private static long ToInt64(object? value)
    {
        return value switch
        {
            sbyte v => v,
            byte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => v,
            long v => v,
            _ => throw new YasnException("Ожидалось целое значение"),
        };
    }

    private static int ToInt(object? value)
    {
        return checked((int)ToInt64(value));
    }

    private object? AddValues(object? a, object? b)
    {
        if (a is string sa && b is string sb)
        {
            return sa + sb;
        }
        if (a is List<object?> la && b is List<object?> lb)
        {
            var merged = new List<object?>(la.Count + lb.Count);
            merged.AddRange(la);
            merged.AddRange(lb);
            return merged;
        }
        if (IsIntegral(a) && IsIntegral(b))
        {
            return ToInt64(a) + ToInt64(b);
        }
        if (IsNumeric(a) && IsNumeric(b))
        {
            return ToDouble(a) + ToDouble(b);
        }
        throw new YasnException("Оператор '+' поддерживает только числа, строки или списки", path: _path);
    }

    private object? SubValues(object? a, object? b)
    {
        if (IsIntegral(a) && IsIntegral(b))
        {
            return ToInt64(a) - ToInt64(b);
        }
        if (IsNumeric(a) && IsNumeric(b))
        {
            return ToDouble(a) - ToDouble(b);
        }
        throw new YasnException("Оператор '-' поддерживает только числа", path: _path);
    }

    private object? MulValues(object? a, object? b)
    {
        if (IsIntegral(a) && IsIntegral(b))
        {
            return ToInt64(a) * ToInt64(b);
        }
        if (IsNumeric(a) && IsNumeric(b))
        {
            return ToDouble(a) * ToDouble(b);
        }
        throw new YasnException("Оператор '*' поддерживает только числа", path: _path);
    }

    private object? DivValues(object? a, object? b)
    {
        if (IsIntegral(a) && IsIntegral(b))
        {
            return (long)Math.Truncate(ToDouble(a) / ToDouble(b));
        }
        if (IsNumeric(a) && IsNumeric(b))
        {
            return ToDouble(a) / ToDouble(b);
        }
        throw new YasnException("Оператор '/' поддерживает только числа", path: _path);
    }

    private object? ModValues(object? a, object? b)
    {
        if (IsIntegral(a) && IsIntegral(b))
        {
            return ToInt64(a) % ToInt64(b);
        }
        if (IsNumeric(a) && IsNumeric(b))
        {
            return ToDouble(a) % ToDouble(b);
        }
        throw new YasnException("Оператор '%' поддерживает только числа", path: _path);
    }

    private object? NegValue(object? value)
    {
        if (IsIntegral(value))
        {
            return -ToInt64(value);
        }
        if (IsNumeric(value))
        {
            return -ToDouble(value);
        }
        throw new YasnException("Унарный '-' поддерживает только числа", path: _path);
    }

    private static int CompareValues(object? a, object? b)
    {
        if (IsNumeric(a) && IsNumeric(b))
        {
            return ToDouble(a).CompareTo(ToDouble(b));
        }
        if (a is string sa && b is string sb)
        {
            return string.CompareOrdinal(sa, sb);
        }
        if (EqualsValue(a, b))
        {
            return 0;
        }
        throw new YasnException("Сравнение поддерживается только для чисел и строк");
    }

    private static bool EqualsValue(object? a, object? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (IsNumeric(a) && IsNumeric(b))
        {
            return Math.Abs(ToDouble(a) - ToDouble(b)) < double.Epsilon;
        }

        if (a is List<object?> leftList && b is List<object?> rightList)
        {
            if (leftList.Count != rightList.Count)
            {
                return false;
            }

            for (var i = 0; i < leftList.Count; i++)
            {
                if (!EqualsValue(leftList[i], rightList[i]))
                {
                    return false;
                }
            }

            return true;
        }

        if (a is Dictionary<object, object?> leftDict && b is Dictionary<object, object?> rightDict)
        {
            if (leftDict.Count != rightDict.Count)
            {
                return false;
            }

            foreach (var (key, leftValue) in leftDict)
            {
                if (!rightDict.TryGetValue(key, out var rightValue))
                {
                    return false;
                }

                if (!EqualsValue(leftValue, rightValue))
                {
                    return false;
                }
            }

            return true;
        }

        return Equals(a, b);
    }

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            long i => i != 0,
            int i => i != 0,
            double d => Math.Abs(d) > double.Epsilon,
            float f => Math.Abs(f) > float.Epsilon,
            string s => s.Length > 0,
            List<object?> list => list.Count > 0,
            Dictionary<object, object?> dict => dict.Count > 0,
            _ => true,
        };
    }

    private static long GetLength(object? value)
    {
        return value switch
        {
            string s => s.Length,
            List<object?> list => list.Count,
            Dictionary<object, object?> dict => dict.Count,
            _ => throw new YasnException("длина(x) поддерживает только Строка/Список/Словарь"),
        };
    }

    private object? IndexGet(object? target, object? index)
    {
        return target switch
        {
            List<object?> list => list[ToInt(index)],
            string text => text[ToInt(index)].ToString(),
            Dictionary<object, object?> dict => dict.TryGetValue(EnsureDictionaryKey(index, "индексация словаря"), out var value)
                ? value
                : throw new YasnException("Ключ не найден в словаре", path: _path),
            _ => throw new YasnException($"INDEX_GET не поддерживается для типа {target?.GetType().Name ?? "null"}", path: _path),
        };
    }

    private void IndexSet(object? target, object? index, object? value)
    {
        switch (target)
        {
            case List<object?> list:
                list[ToInt(index)] = value;
                break;
            case Dictionary<object, object?> dict:
                dict[EnsureDictionaryKey(index, "присваивание по индексу словаря")] = value;
                break;
            default:
                throw new YasnException($"INDEX_SET не поддерживается для типа {target?.GetType().Name ?? "null"}", path: _path);
        }
    }
    private object? BuiltinPrint(List<object?> args, object?[] _)
    {
        Console.WriteLine(string.Join(" ", args.Select(FormatValue)));
        return null;
    }

    private object? BuiltinLen(List<object?> args, object?[] _)
    {
        if (args.Count != 1)
        {
            throw new YasnException("длина(x) принимает ровно 1 аргумент", path: _path);
        }
        return GetLength(args[0]);
    }

    private object? BuiltinRange(List<object?> args, object?[] _)
    {
        if (args.Count != 2)
        {
            throw new YasnException("диапазон(нач, конец) принимает 2 аргумента", path: _path);
        }
        var start = ToInt(args[0]);
        var end = ToInt(args[1]);
        var list = new List<object?>();
        for (var i = start; i < end; i++)
        {
            list.Add((long)i);
        }
        return list;
    }

    private object? BuiltinInput(List<object?> args, object?[] _)
    {
        if (args.Count > 0)
        {
            throw new YasnException("ввод() не принимает аргументы", path: _path);
        }
        return Console.ReadLine() ?? string.Empty;
    }

    private object? BuiltinSleep(List<object?> args, object?[] _)
    {
        if (args.Count != 1)
        {
            throw new YasnException("пауза(мс) принимает ровно 1 аргумент", path: _path);
        }
        var ms = ToInt(args[0]);
        if (ms < 0)
        {
            throw new YasnException("пауза(мс): ожидался Цел >= 0", path: _path);
        }
        Thread.Sleep(ms);
        return null;
    }

    private object? BuiltinToString(List<object?> args, object?[] _)
    {
        if (args.Count != 1)
        {
            throw new YasnException("строка(x) принимает ровно 1 аргумент", path: _path);
        }
        return FormatValue(args[0]);
    }

    private object? BuiltinToInt(List<object?> args, object?[] _)
    {
        if (args.Count != 1)
        {
            throw new YasnException("число(x) принимает ровно 1 аргумент", path: _path);
        }
        var value = args[0];
        if (value is bool b)
        {
            return b ? 1L : 0L;
        }
        if (value is string s)
        {
            var cleaned = s.Trim();
            if (cleaned.Length == 0)
            {
                return 0L;
            }
            if (long.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
            throw new YasnException($"Невозможно преобразовать '{s}' в число", path: _path);
        }
        if (IsIntegral(value))
        {
            return ToInt64(value);
        }
        if (IsNumeric(value))
        {
            return (long)ToDouble(value);
        }
        throw new YasnException($"Невозможно преобразовать '{FormatValue(value)}' в число", path: _path);
    }

    private object? BuiltinAppend(List<object?> args, object?[] _)
    {
        if (args.Count != 2)
        {
            throw new YasnException("добавить(список, элемент) принимает ровно 2 аргумента", path: _path);
        }
        if (args[0] is not List<object?> list)
        {
            throw new YasnException("Первый аргумент добавить(...) должен быть списком", path: _path);
        }
        list.Add(args[1]);
        return null;
    }

    private object? BuiltinRemove(List<object?> args, object?[] _)
    {
        if (args.Count != 2)
        {
            throw new YasnException("удалить(список, индекс) принимает ровно 2 аргумента", path: _path);
        }
        if (args[0] is not List<object?> list)
        {
            throw new YasnException("Первый аргумент удалить(...) должен быть списком", path: _path);
        }
        var idx = ToInt(args[1]);
        if (idx < 0 || idx >= list.Count)
        {
            throw new YasnException($"Индекс {idx} выходит за границы списка длиной {list.Count}", path: _path);
        }
        var removed = list[idx];
        list.RemoveAt(idx);
        return removed;
    }

    private object? BuiltinKeys(List<object?> args, object?[] _)
    {
        if (args.Count != 1)
        {
            throw new YasnException("ключи(словарь) принимает ровно 1 аргумент", path: _path);
        }
        if (args[0] is not Dictionary<object, object?> dict)
        {
            throw new YasnException("Аргумент ключи(...) должен быть словарём", path: _path);
        }
        return dict.Keys.Cast<object?>().ToList();
    }

    private object? BuiltinContains(List<object?> args, object?[] _)
    {
        if (args.Count != 2)
        {
            throw new YasnException("содержит(словарь, ключ) принимает ровно 2 аргумента", path: _path);
        }
        if (args[0] is not Dictionary<object, object?> dict)
        {
            throw new YasnException("Первый аргумент содержит(...) должен быть словарём", path: _path);
        }
        return dict.ContainsKey(EnsureDictionaryKey(args[1], "содержит(словарь, ключ)"));
    }

    private object? BuiltinFileRead(List<object?> args, object?[] _)
    {
        if (args.Count != 1)
        {
            throw new YasnException("файл_читать(путь) принимает ровно 1 аргумент", path: _path);
        }

        var path = ExpectString(args[0], "файл_читать(путь)");
        try
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            throw new YasnException($"Ошибка чтения файла '{path}': {ex.Message}", path: _path);
        }
    }

    private object? BuiltinFileWrite(List<object?> args, object?[] _)
    {
        if (args.Count != 2)
        {
            throw new YasnException("файл_записать(путь, данные) принимает ровно 2 аргумента", path: _path);
        }

        var path = ExpectString(args[0], "файл_записать(путь, данные)");
        var content = ExpectString(args[1], "файл_записать(путь, данные)");
        try
        {
            var fullPath = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(fullPath, content, Encoding.UTF8);
            return null;
        }
        catch (Exception ex)
        {
            throw new YasnException($"Ошибка записи файла '{path}': {ex.Message}", path: _path);
        }
    }

    private object? BuiltinFileExists(List<object?> args, object?[] _)
    {
        if (args.Count != 1)
        {
            throw new YasnException("файл_существует(путь) принимает ровно 1 аргумент", path: _path);
        }

        var path = ExpectString(args[0], "файл_существует(путь)");
        return File.Exists(path);
    }

    private object? BuiltinFileDelete(List<object?> args, object?[] _)
    {
        if (args.Count != 1)
        {
            throw new YasnException("файл_удалить(путь) принимает ровно 1 аргумент", path: _path);
        }

        var path = ExpectString(args[0], "файл_удалить(путь)");
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return null;
        }
        catch (Exception ex)
        {
            throw new YasnException($"Ошибка удаления файла '{path}': {ex.Message}", path: _path);
        }
    }

    private object? BuiltinJsonParse(List<object?> args, object?[] _)
    {
        if (args.Count != 1)
        {
            throw new YasnException("json_разобрать(строка_json) принимает ровно 1 аргумент", path: _path);
        }

        var payload = ExpectString(args[0], "json_разобрать(строка_json)");
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return JsonElementToRuntime(doc.RootElement);
        }
        catch (JsonException ex)
        {
            throw new YasnException($"Некорректный JSON: {ex.Message}", path: _path);
        }
    }

    private object? BuiltinJsonStringify(List<object?> args, object?[] _)
    {
        if (args.Count != 1)
        {
            throw new YasnException("json_строка(значение) принимает ровно 1 аргумент", path: _path);
        }

        var normalized = RuntimeToJsonNode(args[0]);
        return JsonSerializer.Serialize(normalized);
    }

    private object? BuiltinHttpGet(List<object?> args, object?[] _)
    {
        if (args.Count != 1)
        {
            throw new YasnException("http_get(url) принимает ровно 1 аргумент", path: _path);
        }

        var url = ExpectString(args[0], "http_get(url)");
        try
        {
            using var response = SharedHttpClient.GetAsync(url).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return BuildHttpResult(response.StatusCode, body);
        }
        catch (Exception ex)
        {
            throw new YasnException($"HTTP GET ошибка: {ex.Message}", path: _path);
        }
    }

    private object? BuiltinHttpPost(List<object?> args, object?[] _)
    {
        if (args.Count != 2)
        {
            throw new YasnException("http_post(url, body) принимает ровно 2 аргумента", path: _path);
        }

        var url = ExpectString(args[0], "http_post(url, body)");
        var body = ExpectString(args[1], "http_post(url, body)");
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = SharedHttpClient.PostAsync(url, content).GetAwaiter().GetResult();
            var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return BuildHttpResult(response.StatusCode, responseBody);
        }
        catch (Exception ex)
        {
            throw new YasnException($"HTTP POST ошибка: {ex.Message}", path: _path);
        }
    }

    private object? BuiltinNowMs(List<object?> args, object?[] _)
    {
        if (args.Count != 0)
        {
            throw new YasnException("время_мс() не принимает аргументы", path: _path);
        }

        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private object? BuiltinRandomInt(List<object?> args, object?[] _)
    {
        if (args.Count != 2)
        {
            throw new YasnException("случайное_цел(мин, макс) принимает ровно 2 аргумента", path: _path);
        }

        var min = ToInt(args[0]);
        var max = ToInt(args[1]);
        if (max <= min)
        {
            throw new YasnException("случайное_цел(мин, макс): макс должно быть больше мин", path: _path);
        }

        return (long)Random.Shared.Next(min, max);
    }

    private object? BuiltinAssert(List<object?> args, object?[] _)
    {
        if (args.Count is not (1 or 2))
        {
            throw new YasnException("утверждать(условие[, сообщение]) принимает 1 или 2 аргумента", path: _path);
        }

        if (IsTruthy(args[0]))
        {
            return null;
        }

        var message = args.Count == 2 ? ExpectString(args[1], "утверждать(условие[, сообщение])") : "Проверка утверждения завершилась ложью";
        throw new YasnException(message, path: _path);
    }

    private object? BuiltinAssertEqual(List<object?> args, object?[] _)
    {
        if (args.Count is not (2 or 3))
        {
            throw new YasnException("утверждать_равно(факт, ожидание[, сообщение]) принимает 2 или 3 аргумента", path: _path);
        }

        var actual = args[0];
        var expected = args[1];
        if (EqualsValue(actual, expected))
        {
            return null;
        }

        var message = args.Count == 3
            ? ExpectString(args[2], "утверждать_равно(факт, ожидание[, сообщение])")
            : $"Ожидалось {FormatValue(expected)}, получено {FormatValue(actual)}";
        throw new YasnException(message, path: _path);
    }

    private object? BuiltinFail(List<object?> args, object?[] _)
    {
        if (args.Count > 1)
        {
            throw new YasnException("провал([сообщение]) принимает 0 или 1 аргумент", path: _path);
        }

        var message = args.Count == 1
            ? ExpectString(args[0], "провал([сообщение])")
            : "Тест завершён принудительной ошибкой";
        throw new YasnException(message, path: _path);
    }

    private string ExpectString(object? value, string context)
    {
        if (value is string text)
        {
            return text;
        }

        throw new YasnException($"{context}: ожидалась Строка", path: _path);
    }

    private static object? BuildHttpResult(System.Net.HttpStatusCode statusCode, string body)
    {
        return new Dictionary<object, object?>(RuntimeValueComparer.Instance)
        {
            ["status"] = (long)(int)statusCode,
            ["ok"] = ((int)statusCode) is >= 200 and < 300,
            ["body"] = body,
        };
    }

    private static object? JsonElementToRuntime(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var intValue)
                ? intValue
                : element.GetDouble(),
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToRuntime).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                property => (object)property.Name,
                property => JsonElementToRuntime(property.Value),
                RuntimeValueComparer.Instance),
            _ => null,
        };
    }

    private static object? RuntimeToJsonNode(object? value)
    {
        return value switch
        {
            null => null,
            bool => value,
            long => value,
            int => value,
            double => value,
            float v => (double)v,
            decimal v => (double)v,
            string => value,
            TaskHandle task => new Dictionary<string, object?>
            {
                ["task_id"] = task.TaskId,
                ["done"] = task.Future.IsCompleted,
            },
            List<object?> list => list.Select(RuntimeToJsonNode).ToList(),
            Dictionary<object, object?> dict => dict.ToDictionary(
                pair => FormatValue(pair.Key),
                pair => RuntimeToJsonNode(pair.Value)),
            _ => FormatValue(value),
        };
    }

    private object? BuiltinSpawn(List<object?> args, object?[] globalsStore)
    {
        if (args.Count < 1)
        {
            throw new YasnException("запустить(имя, ...args) требует минимум 1 аргумент", path: _path);
        }

        if (args[0] is not string fnName || string.IsNullOrWhiteSpace(fnName))
        {
            throw new YasnException("Первый аргумент запустить(...) должен быть непустой Строка", path: _path);
        }

        if (!_builtins.ContainsKey(fnName) && !_program.Functions.ContainsKey(fnName))
        {
            throw new YasnException($"Неизвестная функция: {fnName}", path: _path);
        }

        var callArgs = args.Skip(1).ToList();
        var snapshot = CloneGlobals(globalsStore);
        var taskId = NextTaskId();
        var cts = new CancellationTokenSource();
        var future = Task.Run(() =>
        {
            cts.Token.ThrowIfCancellationRequested();
            return CallFunctionInternal(fnName, callArgs, snapshot);
        }, cts.Token);

        var handle = new TaskHandle(taskId, future, cts);
        lock (_taskLock)
        {
            _activeTasks.Add(handle);
        }
        return handle;
    }

    private object? BuiltinDone(List<object?> args, object?[] _)
    {
        if (args.Count != 1)
        {
            throw new YasnException("готово(задача) принимает ровно 1 аргумент", path: _path);
        }

        var task = ExpectTaskHandle(args[0], "готово(задача)");
        return task.Future.IsCompleted;
    }

    private object? BuiltinWait(List<object?> args, object?[] _)
    {
        if (args.Count is not (1 or 2))
        {
            throw new YasnException("ожидать(задача[, таймаут_мс]) принимает 1 или 2 аргумента", path: _path);
        }

        var task = ExpectTaskHandle(args[0], "ожидать(задача[, таймаут_мс])");
        int? timeoutMs = null;
        if (args.Count == 2)
        {
            timeoutMs = CoerceNonNegativeInt(args[1], "ожидать(..., таймаут_мс)");
        }

        return WaitTaskResult(task, timeoutMs);
    }

    private object? BuiltinWaitAll(List<object?> args, object?[] _)
    {
        if (args.Count is not (1 or 2))
        {
            throw new YasnException("ожидать_все(список_задач[, таймаут_мс]) принимает 1 или 2 аргумента", path: _path);
        }

        if (args[0] is not List<object?> rawTasks)
        {
            throw new YasnException("Первый аргумент ожидать_все(...) должен быть списком", path: _path);
        }

        int? timeoutMs = null;
        if (args.Count == 2)
        {
            timeoutMs = CoerceNonNegativeInt(args[1], "ожидать_все(..., таймаут_мс)");
        }

        var results = new List<object?>();
        foreach (var value in rawTasks)
        {
            var task = ExpectTaskHandle(value, "ожидать_все(список_задач[, таймаут_мс])");
            results.Add(WaitTaskResult(task, timeoutMs));
        }

        return results;
    }

    private object? BuiltinCancel(List<object?> args, object?[] _)
    {
        if (args.Count != 1)
        {
            throw new YasnException("отменить(задача) принимает ровно 1 аргумент", path: _path);
        }

        var task = ExpectTaskHandle(args[0], "отменить(задача)");
        task.Cancellation.Cancel();
        return true;
    }

    private int CoerceNonNegativeInt(object? value, string context)
    {
        if (value is bool)
        {
            throw new YasnException($"{context}: ожидался Цел >= 0", path: _path);
        }

        int intValue;
        try
        {
            intValue = ToInt(value);
        }
        catch
        {
            throw new YasnException($"{context}: ожидался Цел >= 0", path: _path);
        }

        if (intValue < 0)
        {
            throw new YasnException($"{context}: ожидался Цел >= 0", path: _path);
        }

        return intValue;
    }

    private TaskHandle ExpectTaskHandle(object? value, string context)
    {
        if (value is TaskHandle task)
        {
            return task;
        }

        throw new YasnException($"{context}: ожидался объект типа Задача", path: _path);
    }

    private object? WaitTaskResult(TaskHandle task, int? timeoutMs)
    {
        try
        {
            if (timeoutMs is null)
            {
                task.Future.Wait();
            }
            else if (!task.Future.Wait(timeoutMs.Value))
            {
                throw new TimeoutException();
            }

            return task.Future.Result;
        }
        catch (TimeoutException)
        {
            throw new YasnException($"Истёк таймаут ожидания задачи #{task.TaskId}", path: _path);
        }
        catch (AggregateException ex)
        {
            var inner = ex.InnerException;
            if (inner is OperationCanceledException)
            {
                throw new YasnException($"Задача #{task.TaskId} была отменена", path: _path);
            }

            if (inner is YasnException yasn)
            {
                throw yasn;
            }

            throw new YasnException($"Задача #{task.TaskId} завершилась с ошибкой: {inner?.Message ?? ex.Message}", path: _path);
        }
        catch (OperationCanceledException)
        {
            throw new YasnException($"Задача #{task.TaskId} была отменена", path: _path);
        }
    }

    private int NextTaskId()
    {
        lock (_taskLock)
        {
            _taskCounter++;
            return _taskCounter;
        }
    }

    private object?[] CloneGlobals(object?[] values)
    {
        var clone = new object?[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            clone[i] = CloneValue(values[i]);
        }

        return clone;
    }

    private object? CloneValue(object? value)
    {
        return value switch
        {
            null => null,
            bool => value,
            long => value,
            int => value,
            double => value,
            float => value,
            decimal => value,
            string => value,
            TaskHandle => value,
            List<object?> list => list.Select(CloneValue).ToList(),
            Dictionary<object, object?> dict => CloneDictionary(dict),
            _ => value,
        };
    }

    private object EnsureDictionaryKey(object? key, string context)
    {
        if (key is null)
        {
            throw new YasnException($"{context}: ключ словаря не может быть пусто", path: _path);
        }

        return key;
    }

    private Dictionary<object, object?> CloneDictionary(Dictionary<object, object?> source)
    {
        var clone = new Dictionary<object, object?>(RuntimeValueComparer.Instance);
        foreach (var (key, value) in source)
        {
            var clonedKey = CloneValue(key);
            if (clonedKey is null)
            {
                throw new YasnException("Ключ словаря не может быть пусто", path: _path);
            }

            clone[clonedKey] = CloneValue(value);
        }

        return clone;
    }

    public static string FormatValue(object? value)
    {
        return value switch
        {
            null => "пусто",
            true => "истина",
            false => "ложь",
            TaskHandle task => FormatTask(task),
            List<object?> list => "[" + string.Join(", ", list.Select(FormatValue)) + "]",
            Dictionary<object, object?> dict => "{ " + string.Join(", ", dict.Select(pair => $"{FormatValue(pair.Key)}: {FormatValue(pair.Value)}")) + " }",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static string FormatTask(TaskHandle task)
    {
        var status = task.Future.IsCanceled
            ? "отменена"
            : task.Future.IsCompleted
                ? "готово"
                : "выполняется";
        return $"<задача #{task.TaskId}: {status}>";
    }

    public void Dispose()
    {
        lock (_taskLock)
        {
            foreach (var task in _activeTasks)
            {
                try
                {
                    task.Cancellation.Cancel();
                }
                catch
                {
                    // ignore cancellation failures
                }
                finally
                {
                    task.Cancellation.Dispose();
                }
            }

            _activeTasks.Clear();
        }
    }
}

internal sealed class RuntimeValueComparer : IEqualityComparer<object>
{
    public static readonly RuntimeValueComparer Instance = new();

    public new bool Equals(object? x, object? y)
    {
        return ReferenceEquals(x, y) || x?.Equals(y) == true;
    }

    public int GetHashCode(object obj)
    {
        return obj.GetHashCode();
    }
}


