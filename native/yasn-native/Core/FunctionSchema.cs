using YasnNative.Bytecode;

namespace YasnNative.Core;

public sealed record FunctionParamSchema(string Name, string Type);

public sealed record FunctionSchema(string Name, List<FunctionParamSchema> Params, string ReturnType, bool IsAsync)
{
    public string Signature
    {
        get
        {
            var args = string.Join(", ", Params.Select(p => $"{p.Name}: {p.Type}"));
            var asyncPrefix = IsAsync ? "async " : string.Empty;
            return $"{asyncPrefix}{Name}({args}) -> {ReturnType}";
        }
    }

    public Dictionary<string, object?> ToJsonObject()
    {
        return new Dictionary<string, object?>
        {
            ["name"] = Name,
            ["params"] = Params.Select(p => new Dictionary<string, object?>
            {
                ["name"] = p.Name,
                ["type"] = p.Type,
            }).ToList(),
            ["returnType"] = ReturnType,
            ["isAsync"] = IsAsync,
            ["signature"] = Signature,
        };
    }
}

public static class FunctionSchemaBuilder
{
    public static List<FunctionSchema> FromProgramNode(ProgramNode program)
    {
        return program.Statements
            .OfType<FuncDeclStmt>()
            .OrderBy(fn => fn.Name, StringComparer.Ordinal)
            .Select(fn => new FunctionSchema(
                fn.Name,
                fn.Params
                    .Select(param => new FunctionParamSchema(param.Name, FormatTypeNode(param.TypeNode)))
                    .ToList(),
                FormatTypeNode(fn.ReturnType),
                fn.IsAsync))
            .ToList();
    }

    public static List<FunctionSchema> FromProgramBytecode(ProgramBC program)
    {
        return program.Functions.Values
            .OrderBy(fn => fn.Name, StringComparer.Ordinal)
            .Select(fn => new FunctionSchema(
                fn.Name,
                fn.Params.Select(param => new FunctionParamSchema(param, "Любой")).ToList(),
                "Любой",
                false))
            .ToList();
    }

    public static List<Dictionary<string, object?>> ToJsonList(IEnumerable<FunctionSchema> schema)
    {
        return schema.Select(item => item.ToJsonObject()).ToList();
    }

    private static string FormatTypeNode(TypeNode node)
    {
        return node switch
        {
            PrimitiveTypeNode primitive => primitive.Name,
            ListTypeNode list => $"Список<{FormatTypeNode(list.Element)}>",
            DictTypeNode dict => $"Словарь<{FormatTypeNode(dict.Key)}, {FormatTypeNode(dict.Value)}>",
            UnionTypeNode union => string.Join(" | ", union.Variants.Select(FormatTypeNode)),
            _ => "Любой",
        };
    }
}
