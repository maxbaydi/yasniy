using System.Text;
using YasnNative.Bytecode;

namespace YasnNative.Core;

public sealed record SchemaTypeNode(
    string Kind,
    string? Name = null,
    SchemaTypeNode? Element = null,
    SchemaTypeNode? Key = null,
    SchemaTypeNode? Value = null,
    List<SchemaTypeNode>? Variants = null)
{
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

    public bool IsNullable =>
        string.Equals(Kind, "union", StringComparison.Ordinal) &&
        (Variants?.Any(static variant => variant.IsNullPrimitive) ?? false);

    public bool IsNullPrimitive =>
        string.Equals(Kind, "primitive", StringComparison.Ordinal) &&
        string.Equals(Name, "Пусто", StringComparison.Ordinal);

    public string Display => FunctionSchemaBuilder.FormatSchemaType(this);

    public Dictionary<string, object?> ToJsonObject()
    {
        var json = new Dictionary<string, object?>
        {
            ["kind"] = Kind,
            ["display"] = Display,
            ["nullable"] = IsNullable,
        };

        if (!string.IsNullOrWhiteSpace(Name))
        {
            json["name"] = Name;
        }

        if (Element is not null)
        {
            json["element"] = Element.ToJsonObject();
        }

        if (Key is not null)
        {
            json["key"] = Key.ToJsonObject();
        }

        if (Value is not null)
        {
            json["value"] = Value.ToJsonObject();
        }

        if (Variants is { Count: > 0 })
        {
            json["variants"] = Variants
                .Select(static variant => (object?)variant.ToJsonObject())
                .ToList();
        }

        return json;
    }

    public static SchemaTypeNode Primitive(string name)
    {
        return new SchemaTypeNode("primitive", Name: name);
    }

    public static SchemaTypeNode List(SchemaTypeNode element)
    {
        return new SchemaTypeNode("list", Element: element);
    }

    public static SchemaTypeNode Dict(SchemaTypeNode key, SchemaTypeNode value)
    {
        return new SchemaTypeNode("dict", Key: key, Value: value);
    }

    public static SchemaTypeNode Union(IEnumerable<SchemaTypeNode> variants)
    {
        var flattened = new List<SchemaTypeNode>();
        foreach (var variant in variants)
        {
            if (string.Equals(variant.Kind, "union", StringComparison.Ordinal) && variant.Variants is { Count: > 0 })
            {
                flattened.AddRange(variant.Variants);
            }
            else
            {
                flattened.Add(variant);
            }
        }

        var unique = new List<SchemaTypeNode>();
        foreach (var variant in flattened)
        {
            if (unique.Any(existing => TypeEquals(existing, variant)))
            {
                continue;
            }

            unique.Add(variant);
        }

        if (unique.Count == 1)
        {
            return unique[0];
        }

        return new SchemaTypeNode("union", Variants: unique);
    }

    public static SchemaTypeNode Any()
    {
        return Primitive("Любой");
    }

    public static SchemaTypeNode FromTypeNode(TypeNode node)
    {
        return node switch
        {
            PrimitiveTypeNode primitive => Primitive(primitive.Name),
            ListTypeNode list => List(FromTypeNode(list.Element)),
            DictTypeNode dict => Dict(FromTypeNode(dict.Key), FromTypeNode(dict.Value)),
            UnionTypeNode union => Union(union.Variants.Select(FromTypeNode)),
            _ => Any(),
        };
    }

    public static SchemaTypeNode FromLegacyLabel(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return Any();
        }

        var unionParts = SplitTopLevel(text, '|');
        if (unionParts.Count > 1)
        {
            return Union(unionParts.Select(FromLegacyLabel));
        }

        if (text.EndsWith("?", StringComparison.Ordinal))
        {
            return Union([FromLegacyLabel(text[..^1]), Primitive("Пусто")]);
        }

        if (TryParseGeneric(text, "Список", out var listArgs) && listArgs.Count == 1)
        {
            return List(FromLegacyLabel(listArgs[0]));
        }

        if (TryParseGeneric(text, "Словарь", out var dictArgs) && dictArgs.Count == 2)
        {
            return Dict(FromLegacyLabel(dictArgs[0]), FromLegacyLabel(dictArgs[1]));
        }

        if (PrimitiveNames.Contains(text))
        {
            return Primitive(text);
        }

        return Any();
    }

    private static bool TryParseGeneric(string text, string genericName, out List<string> args)
    {
        args = [];
        if (!text.StartsWith(genericName, StringComparison.Ordinal))
        {
            return false;
        }

        var payload = SliceBracketPayload(text, '<', '>')
            ?? SliceBracketPayload(text, '[', ']');
        if (payload is null)
        {
            return false;
        }

        args = SplitTopLevel(payload, ',');
        return true;
    }

    private static string? SliceBracketPayload(string text, char open, char close)
    {
        var openPos = text.IndexOf(open, StringComparison.Ordinal);
        if (openPos < 0 || !text.EndsWith(close))
        {
            return null;
        }

        var inner = text[(openPos + 1)..^1];
        return inner.Trim();
    }

    private static List<string> SplitTopLevel(string text, char separator)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var angleDepth = 0;
        var squareDepth = 0;
        var roundDepth = 0;

        foreach (var ch in text)
        {
            switch (ch)
            {
                case '<':
                    angleDepth++;
                    current.Append(ch);
                    continue;
                case '>':
                    angleDepth = Math.Max(0, angleDepth - 1);
                    current.Append(ch);
                    continue;
                case '[':
                    squareDepth++;
                    current.Append(ch);
                    continue;
                case ']':
                    squareDepth = Math.Max(0, squareDepth - 1);
                    current.Append(ch);
                    continue;
                case '(':
                    roundDepth++;
                    current.Append(ch);
                    continue;
                case ')':
                    roundDepth = Math.Max(0, roundDepth - 1);
                    current.Append(ch);
                    continue;
                default:
                    break;
            }

            if (ch == separator && angleDepth == 0 && squareDepth == 0 && roundDepth == 0)
            {
                var piece = current.ToString().Trim();
                if (piece.Length > 0)
                {
                    result.Add(piece);
                }

                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        var tail = current.ToString().Trim();
        if (tail.Length > 0)
        {
            result.Add(tail);
        }

        return result;
    }

    private static bool TypeEquals(SchemaTypeNode left, SchemaTypeNode right)
    {
        if (!string.Equals(left.Kind, right.Kind, StringComparison.Ordinal))
        {
            return false;
        }

        return left.Kind switch
        {
            "primitive" => string.Equals(left.Name, right.Name, StringComparison.Ordinal),
            "list" => left.Element is not null && right.Element is not null && TypeEquals(left.Element, right.Element),
            "dict" => left.Key is not null && left.Value is not null && right.Key is not null && right.Value is not null
                && TypeEquals(left.Key, right.Key)
                && TypeEquals(left.Value, right.Value),
            "union" => UnionEquals(left.Variants, right.Variants),
            _ => false,
        };
    }

    private static bool UnionEquals(IReadOnlyList<SchemaTypeNode>? left, IReadOnlyList<SchemaTypeNode>? right)
    {
        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        return left.All(item => right.Any(candidate => TypeEquals(item, candidate)));
    }
}

public sealed record FunctionParamSchema(
    string Name,
    string Type,
    SchemaTypeNode? TypeNode = null,
    Dictionary<string, object?>? Ui = null)
{
    public SchemaTypeNode ResolvedTypeNode => TypeNode ?? SchemaTypeNode.FromLegacyLabel(Type);

    public Dictionary<string, object?> ToJsonObject()
    {
        return new Dictionary<string, object?>
        {
            ["name"] = Name,
            ["type"] = Type,
            ["typeNode"] = ResolvedTypeNode.ToJsonObject(),
            ["ui"] = Ui ?? FunctionSchemaBuilder.BuildParamUiHints(ResolvedTypeNode),
        };
    }
}

public sealed record FunctionSchema(
    string Name,
    List<FunctionParamSchema> Params,
    string ReturnType,
    bool IsAsync,
    bool IsPublicApi = true,
    SchemaTypeNode? ReturnTypeNode = null,
    Dictionary<string, object?>? Ui = null)
{
    public SchemaTypeNode ResolvedReturnTypeNode => ReturnTypeNode ?? SchemaTypeNode.FromLegacyLabel(ReturnType);

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
            ["params"] = Params.Select(static p => (object?)p.ToJsonObject()).ToList(),
            ["returnType"] = ReturnType,
            ["returnTypeNode"] = ResolvedReturnTypeNode.ToJsonObject(),
            ["isAsync"] = IsAsync,
            ["isPublicApi"] = IsPublicApi,
            ["signature"] = Signature,
            ["schemaVersion"] = 2,
            ["ui"] = Ui ?? FunctionSchemaBuilder.BuildFunctionUiHints(IsPublicApi),
        };
    }
}

public static class FunctionSchemaBuilder
{
    public static List<FunctionSchema> FromProgramNode(ProgramNode program)
    {
        var functions = program.Statements
            .OfType<FuncDeclStmt>()
            .ToList();
        var hasExplicitExports = functions.Any(static fn => fn.Exported);

        return functions
            .Where(fn => IsVisibleForUi(fn, hasExplicitExports))
            .OrderBy(fn => fn.Name, StringComparer.Ordinal)
            .Select(fn => BuildFromFuncDecl(fn, hasExplicitExports))
            .ToList();
    }

    public static List<FunctionSchema> FromProgramBytecode(ProgramBC program)
    {
        return program.Functions.Values
            .Where(static fn => !IsInternalName(fn.Name) && !string.Equals(fn.Name, "main", StringComparison.Ordinal))
            .OrderBy(fn => fn.Name, StringComparer.Ordinal)
            .Select(fn => new FunctionSchema(
                fn.Name,
                fn.Params
                    .Select(param => new FunctionParamSchema(
                        param,
                        "Любой",
                        SchemaTypeNode.Any(),
                        BuildParamUiHints(SchemaTypeNode.Any())))
                    .ToList(),
                "Любой",
                false,
                IsPublicApi: true,
                ReturnTypeNode: SchemaTypeNode.Any(),
                Ui: BuildFunctionUiHints(isPublicApi: true)))
            .ToList();
    }

    public static List<FunctionSchema> NormalizeForUiApi(IEnumerable<FunctionSchema> schema)
    {
        return schema
            .Select(item =>
            {
                var normalizedParams = item.Params
                    .Select(param =>
                    {
                        var typeNode = param.ResolvedTypeNode;
                        return param with
                        {
                            TypeNode = typeNode,
                            Ui = param.Ui ?? BuildParamUiHints(typeNode),
                        };
                    })
                    .ToList();

                var normalizedReturnTypeNode = item.ResolvedReturnTypeNode;
                return item with
                {
                    Params = normalizedParams,
                    ReturnTypeNode = normalizedReturnTypeNode,
                    Ui = item.Ui ?? BuildFunctionUiHints(item.IsPublicApi),
                };
            })
            .Where(item => item.IsPublicApi && !IsInternalName(item.Name) && !string.Equals(item.Name, "main", StringComparison.Ordinal))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToList();
    }

    public static List<Dictionary<string, object?>> ToJsonList(IEnumerable<FunctionSchema> schema)
    {
        return schema.Select(static item => item.ToJsonObject()).ToList();
    }

    public static bool IsValueAssignableToType(object? value, SchemaTypeNode type)
    {
        return type.Kind switch
        {
            "primitive" => IsPrimitiveAssignable(value, type.Name),
            "list" => value is List<object?> list &&
                type.Element is not null &&
                list.All(item => IsValueAssignableToType(item, type.Element)),
            "dict" => value is Dictionary<object, object?> dict &&
                type.Key is not null &&
                type.Value is not null &&
                dict.All(pair =>
                    IsValueAssignableToType(pair.Key, type.Key) &&
                    IsValueAssignableToType(pair.Value, type.Value)),
            "union" => (type.Variants?.Any(variant => IsValueAssignableToType(value, variant)) ?? false),
            _ => true,
        };
    }

    public static string FormatSchemaType(SchemaTypeNode type)
    {
        return type.Kind switch
        {
            "primitive" => type.Name ?? "Любой",
            "list" when type.Element is not null => $"Список<{FormatSchemaType(type.Element)}>",
            "dict" when type.Key is not null && type.Value is not null
                => $"Словарь<{FormatSchemaType(type.Key)}, {FormatSchemaType(type.Value)}>",
            "union" when type.Variants is { Count: > 0 }
                => string.Join(" | ", type.Variants.Select(FormatSchemaType)),
            _ => "Любой",
        };
    }

    internal static Dictionary<string, object?> BuildParamUiHints(SchemaTypeNode type)
    {
        var control = type.Kind switch
        {
            "list" or "dict" => "json",
            "union" => "select",
            "primitive" when string.Equals(type.Name, "Лог", StringComparison.Ordinal) => "checkbox",
            "primitive" when string.Equals(type.Name, "Цел", StringComparison.Ordinal) || string.Equals(type.Name, "Дроб", StringComparison.Ordinal) => "number",
            "primitive" when string.Equals(type.Name, "Любой", StringComparison.Ordinal) => "json",
            _ => "text",
        };

        return new Dictionary<string, object?>
        {
            ["control"] = control,
            ["placeholder"] = PlaceholderForType(type),
            ["nullable"] = type.IsNullable,
            ["required"] = !type.IsNullable,
        };
    }

    internal static Dictionary<string, object?> BuildFunctionUiHints(bool isPublicApi)
    {
        return new Dictionary<string, object?>
        {
            ["exposure"] = isPublicApi ? "public" : "internal",
        };
    }

    private static FunctionSchema BuildFromFuncDecl(FuncDeclStmt fn, bool hasExplicitExports)
    {
        var parameters = fn.Params
            .Select(param =>
            {
                var schemaType = SchemaTypeNode.FromTypeNode(param.TypeNode);
                return new FunctionParamSchema(
                    param.Name,
                    FormatTypeNode(param.TypeNode),
                    schemaType,
                    BuildParamUiHints(schemaType));
            })
            .ToList();

        var returnType = SchemaTypeNode.FromTypeNode(fn.ReturnType);
        var isPublicApi = hasExplicitExports ? fn.Exported : true;

        return new FunctionSchema(
            fn.Name,
            parameters,
            FormatTypeNode(fn.ReturnType),
            fn.IsAsync,
            IsPublicApi: isPublicApi,
            ReturnTypeNode: returnType,
            Ui: BuildFunctionUiHints(isPublicApi));
    }

    private static bool IsVisibleForUi(FuncDeclStmt fn, bool hasExplicitExports)
    {
        if (IsInternalName(fn.Name) || string.Equals(fn.Name, "main", StringComparison.Ordinal))
        {
            return false;
        }

        return !hasExplicitExports || fn.Exported;
    }

    private static bool IsInternalName(string name)
    {
        return name.StartsWith("__мод_", StringComparison.Ordinal);
    }

    private static bool IsPrimitiveAssignable(object? value, string? primitiveName)
    {
        return primitiveName switch
        {
            null => true,
            "Любой" => true,
            "Пусто" => value is null,
            "Лог" => value is bool,
            "Строка" => value is string,
            "Цел" => value is int or long,
            "Дроб" => value is int or long or float or double or decimal,
            "Задача" => value is not null && string.Equals(value.GetType().Name, "TaskHandle", StringComparison.Ordinal),
            _ => true,
        };
    }

    private static string PlaceholderForType(SchemaTypeNode type)
    {
        if (type.Kind == "primitive")
        {
            return type.Name switch
            {
                "Цел" => "42",
                "Дроб" => "3.14",
                "Лог" => "true | false",
                "Строка" => "value",
                "Любой" => "{\"key\":\"value\"}",
                _ => "value",
            };
        }

        if (type.Kind == "list")
        {
            return "[1, 2, 3]";
        }

        if (type.Kind == "dict")
        {
            return "{\"key\":\"value\"}";
        }

        if (type.Kind == "union")
        {
            return string.Join(" | ", type.Variants?.Select(FormatSchemaType) ?? []);
        }

        return "value";
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
