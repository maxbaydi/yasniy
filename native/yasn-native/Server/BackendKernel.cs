using YasnNative.Bytecode;
using YasnNative.Core;
using YasnNative.Runtime;

namespace YasnNative.Server;

public sealed record BackendCallValidationError(int StatusCode, string Code, string Message);

public sealed class BackendKernel
{
    private readonly VirtualMachine _vm;
    private readonly List<FunctionSchema> _schema;
    private readonly Dictionary<string, FunctionSchema> _schemaByName;

    private BackendKernel(string sourcePath, VirtualMachine vm, List<FunctionSchema> schema)
    {
        SourcePath = sourcePath;
        _vm = vm;
        _schema = FunctionSchemaBuilder.NormalizeForUiApi(schema);
        _schemaByName = _schema.ToDictionary(static item => item.Name, StringComparer.Ordinal);
    }

    public string SourcePath { get; }

    public static BackendKernel FromFile(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        var source = File.ReadAllText(fullPath);
        var loadedProgram = Pipeline.LoadProgram(source, fullPath);
        var program = Pipeline.CompileProgram(loadedProgram, fullPath);
        var vm = new VirtualMachine(program, fullPath);
        var schema = FunctionSchemaBuilder.FromProgramNode(loadedProgram);
        return new BackendKernel(fullPath, vm, schema);
    }

    public static BackendKernel FromProgram(ProgramBC program, string sourcePath = "<bundle>", IEnumerable<FunctionSchema>? schema = null)
    {
        var vm = new VirtualMachine(program, sourcePath);
        var effectiveSchema = schema?.ToList() ?? FunctionSchemaBuilder.FromProgramBytecode(program);
        return new BackendKernel(sourcePath, vm, effectiveSchema);
    }

    public List<string> ListFunctions()
    {
        return _schema.Select(static item => item.Name).ToList();
    }

    public List<FunctionSchema> ListSchema()
    {
        return _schema
            .Select(CloneSchema)
            .ToList();
    }

    public bool TryGetSchema(string functionName, out FunctionSchema schema)
    {
        return _schemaByName.TryGetValue(functionName, out schema!);
    }

    public bool TryPrepareCall(
        string functionName,
        object? argsRaw,
        object? namedArgsRaw,
        out List<object?> args,
        out BackendCallValidationError? error)
    {
        args = [];
        error = null;

        if (!TryGetSchema(functionName, out var schema))
        {
            error = new BackendCallValidationError(404, "unknown_function", $"Function not found in public UI API: {functionName}");
            return false;
        }

        if (argsRaw is not null && namedArgsRaw is not null)
        {
            error = new BackendCallValidationError(400, "invalid_request", "Use either 'args' or 'named_args', but not both");
            return false;
        }

        if (namedArgsRaw is not null)
        {
            if (!TryBuildArgsFromNamed(schema, namedArgsRaw, out args, out var namedError))
            {
                error = namedError;
                return false;
            }
        }
        else if (argsRaw is null)
        {
            args = [];
        }
        else if (argsRaw is List<object?> list)
        {
            args = [.. list];
        }
        else
        {
            error = new BackendCallValidationError(400, "invalid_request", "Field 'args' must be an array");
            return false;
        }

        if (!TryValidateArguments(schema, args, out var validationMessage))
        {
            error = new BackendCallValidationError(400, "invalid_arguments", validationMessage);
            return false;
        }

        return true;
    }

    public object? Call(string functionName, List<object?>? args = null, bool resetState = true, bool awaitResult = true)
    {
        var callArgs = args is null ? new List<object?>() : new List<object?>(args);
        if (awaitResult)
        {
            return _vm.CallFunction(functionName, callArgs, resetState);
        }

        var spawnArgs = new List<object?>(callArgs.Count + 1) { functionName };
        spawnArgs.AddRange(callArgs);
        return _vm.CallFunction("запустить", spawnArgs, resetState);
    }

    private static FunctionSchema CloneSchema(FunctionSchema item)
    {
        return item with
        {
            Params = item.Params
                .Select(param => param with
                {
                    Ui = param.Ui is null ? null : new Dictionary<string, object?>(param.Ui, StringComparer.Ordinal),
                })
                .ToList(),
            Ui = item.Ui is null ? null : new Dictionary<string, object?>(item.Ui, StringComparer.Ordinal),
        };
    }

    private static bool TryBuildArgsFromNamed(
        FunctionSchema schema,
        object? namedArgsRaw,
        out List<object?> args,
        out BackendCallValidationError? error)
    {
        args = [];
        error = null;

        if (namedArgsRaw is not Dictionary<object, object?> named)
        {
            error = new BackendCallValidationError(400, "invalid_request", "Field 'named_args' must be an object");
            return false;
        }

        var lookup = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in named)
        {
            if (pair.Key is not string key || string.IsNullOrWhiteSpace(key))
            {
                error = new BackendCallValidationError(400, "invalid_request", "Field 'named_args' may contain only non-empty string keys");
                return false;
            }

            lookup[key] = pair.Value;
        }

        foreach (var param in schema.Params)
        {
            if (!lookup.TryGetValue(param.Name, out var value))
            {
                error = new BackendCallValidationError(400, "invalid_arguments", $"Missing named argument: {param.Name}");
                return false;
            }

            args.Add(value);
            lookup.Remove(param.Name);
        }

        if (lookup.Count > 0)
        {
            var unknown = string.Join(", ", lookup.Keys.OrderBy(static key => key, StringComparer.Ordinal));
            error = new BackendCallValidationError(400, "invalid_arguments", $"Unknown named arguments: {unknown}");
            return false;
        }

        return true;
    }

    private static bool TryValidateArguments(FunctionSchema schema, List<object?> args, out string message)
    {
        if (args.Count != schema.Params.Count)
        {
            message = $"Function '{schema.Name}' expects {schema.Params.Count} argument(s), received {args.Count}";
            return false;
        }

        for (var i = 0; i < schema.Params.Count; i++)
        {
            var param = schema.Params[i];
            var typeNode = param.ResolvedTypeNode;
            var value = args[i];
            if (FunctionSchemaBuilder.IsValueAssignableToType(value, typeNode))
            {
                continue;
            }

            var expected = FunctionSchemaBuilder.FormatSchemaType(typeNode);
            message = $"Argument '{param.Name}' must be '{expected}', got '{DescribeRuntimeValue(value)}'";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static string DescribeRuntimeValue(object? value)
    {
        return value switch
        {
            null => "Пусто",
            bool => "Лог",
            string => "Строка",
            int or long => "Цел",
            float or double or decimal => "Дроб",
            List<object?> => "Список",
            Dictionary<object, object?> => "Словарь",
            _ => value.GetType().Name,
        };
    }
}
