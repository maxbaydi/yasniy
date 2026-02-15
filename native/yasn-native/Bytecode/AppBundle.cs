using System.Buffers.Binary;
using System.Text.Json;
using YasnNative.Core;

namespace YasnNative.Bytecode;

public sealed record AppBundle(
    string Name,
    int Version,
    byte[] Bytecode,
    string? DisplayName = null,
    string? Description = null,
    string? AppVersion = null,
    string? Publisher = null,
    List<FunctionSchema>? Schema = null,
    byte[]? UiDistZip = null);

public sealed record AppBundleMetadata(
    string Name,
    string? DisplayName = null,
    string? Description = null,
    string? AppVersion = null,
    string? Publisher = null,
    List<FunctionSchema>? Schema = null);

public static class AppBundleCodec
{
    private static readonly byte[] AppMagic = "YASNYAP1"u8.ToArray();

    public const int AppVersion = 2;

    public static byte[] CreateBundle(string name, byte[] bytecode)
    {
        return CreateBundle(new AppBundleMetadata(name), bytecode, uiDistZip: null);
    }

    public static byte[] CreateBundle(AppBundleMetadata metadata, byte[] bytecode)
    {
        return CreateBundle(metadata, bytecode, uiDistZip: null);
    }

    public static byte[] CreateBundle(AppBundleMetadata metadata, byte[] bytecode, byte[]? uiDistZip)
    {
        var metaObject = new Dictionary<string, object?>
        {
            ["name"] = metadata.Name,
            ["version"] = AppVersion,
        };

        AddIfNotEmpty(metaObject, "displayName", metadata.DisplayName);
        AddIfNotEmpty(metaObject, "description", metadata.Description);
        AddIfNotEmpty(metaObject, "appVersion", metadata.AppVersion);
        AddIfNotEmpty(metaObject, "publisher", metadata.Publisher);
        AddSchemaIfPresent(metaObject, metadata.Schema);

        var meta = JsonSerializer.SerializeToUtf8Bytes(
            metaObject,
            new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = false,
            });

        var uiBytes = uiDistZip ?? [];
        var output = new byte[AppMagic.Length + 4 + meta.Length + 4 + bytecode.Length + 4 + uiBytes.Length];
        var offset = 0;
        Buffer.BlockCopy(AppMagic, 0, output, offset, AppMagic.Length);
        offset += AppMagic.Length;

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(offset, 4), (uint)meta.Length);
        offset += 4;
        Buffer.BlockCopy(meta, 0, output, offset, meta.Length);
        offset += meta.Length;

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(offset, 4), (uint)bytecode.Length);
        offset += 4;
        Buffer.BlockCopy(bytecode, 0, output, offset, bytecode.Length);
        offset += bytecode.Length;

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(offset, 4), (uint)uiBytes.Length);
        offset += 4;
        if (uiBytes.Length > 0)
        {
            Buffer.BlockCopy(uiBytes, 0, output, offset, uiBytes.Length);
        }

        return output;
    }

    public static AppBundle ReadBundle(byte[] blob, string? path = null)
    {
        if (blob.Length < AppMagic.Length + 8)
        {
            throw new YasnException("Файл приложения слишком короткий", path: path);
        }

        if (!blob.AsSpan(0, AppMagic.Length).SequenceEqual(AppMagic))
        {
            throw new YasnException("Некорректная сигнатура файла приложения", path: path);
        }

        var offset = AppMagic.Length;
        var metaLength = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
        offset += 4;
        if (offset + metaLength + 4 > blob.Length)
        {
            throw new YasnException("Повреждён заголовок метаданных приложения", path: path);
        }

        var metaRaw = blob.AsSpan(offset, checked((int)metaLength)).ToArray();
        offset += (int)metaLength;

        string name;
        int version;
        string? displayName;
        string? description;
        string? appVersion;
        string? publisher;
        List<FunctionSchema>? schema;
        try
        {
            using var doc = JsonDocument.Parse(metaRaw);
            var root = doc.RootElement;
            name = root.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? "app"
                : "app";
            version = root.TryGetProperty("version", out var versionElement)
                ? versionElement.GetInt32()
                : 0;
            displayName = ReadOptionalString(root, "displayName", path);
            description = ReadOptionalString(root, "description", path);
            appVersion = ReadOptionalString(root, "appVersion", path);
            publisher = ReadOptionalString(root, "publisher", path);
            schema = ReadSchema(root, path);
        }
        catch (Exception ex)
        {
            throw new YasnException($"Не удалось разобрать метаданные приложения: {ex.Message}", path: path);
        }

        if (version is not (1 or AppVersion))
        {
            throw new YasnException(
                $"Неподдерживаемая версия формата приложения: {version}, ожидается 1 или {AppVersion}",
                path: path);
        }

        var bytecodeLength = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
        offset += 4;
        if (offset + bytecodeLength > blob.Length)
        {
            throw new YasnException("Некорректная длина байткода в приложении", path: path);
        }

        var bytecode = blob.AsSpan(offset, checked((int)bytecodeLength)).ToArray();
        offset += (int)bytecodeLength;

        byte[]? uiDistZip = null;
        if (version == AppVersion)
        {
            if (offset + 4 > blob.Length)
            {
                throw new YasnException("Повреждён блок UI-ресурсов в приложении", path: path);
            }

            var uiLength = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
            offset += 4;
            if (offset + uiLength != blob.Length)
            {
                throw new YasnException("Некорректная длина UI-архива в приложении", path: path);
            }

            if (uiLength > 0)
            {
                uiDistZip = blob.AsSpan(offset, checked((int)uiLength)).ToArray();
            }
        }
        else if (offset != blob.Length)
        {
            throw new YasnException("Некорректная длина байткода в приложении", path: path);
        }

        return new AppBundle(name, version, bytecode, displayName, description, appVersion, publisher, schema, uiDistZip);
    }

    public static (AppBundle Bundle, ProgramBC Program) DecodeBundleToProgram(byte[] blob, string? path = null)
    {
        var bundle = ReadBundle(blob, path);
        var program = BytecodeCodec.DecodeProgram(bundle.Bytecode, path);
        return (bundle, program);
    }

    private static void AddIfNotEmpty(Dictionary<string, object?> metaObject, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metaObject[key] = value;
        }
    }

    private static void AddSchemaIfPresent(Dictionary<string, object?> metaObject, List<FunctionSchema>? schema)
    {
        if (schema is null || schema.Count == 0)
        {
            return;
        }

        metaObject["schema"] = FunctionSchemaBuilder.ToJsonList(schema);
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName, string? path)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        throw new YasnException($"Поле метаданных '{propertyName}' должно быть строкой", path: path);
    }

    private static List<FunctionSchema>? ReadSchema(JsonElement root, string? path)
    {
        if (!root.TryGetProperty("schema", out var schemaElement) || schemaElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (schemaElement.ValueKind != JsonValueKind.Array)
        {
            throw new YasnException("Поле метаданных 'schema' должно быть массивом", path: path);
        }

        var result = new List<FunctionSchema>();
        foreach (var fnElement in schemaElement.EnumerateArray())
        {
            if (fnElement.ValueKind != JsonValueKind.Object)
            {
                throw new YasnException("Элемент schema должен быть объектом", path: path);
            }

            var name = RequireString(fnElement, "name", path);
            var returnType = TryReadString(fnElement, "returnType", path) ?? "Любой";
            var returnTypeNode = ReadOptionalSchemaTypeNode(fnElement, "returnTypeNode", path);
            if (string.IsNullOrWhiteSpace(returnType) && returnTypeNode is not null)
            {
                returnType = FunctionSchemaBuilder.FormatSchemaType(returnTypeNode);
            }

            var isAsync = ReadOptionalBool(fnElement, "isAsync", defaultValue: false, path);
            var isPublicApi = ReadOptionalBool(fnElement, "isPublicApi", defaultValue: true, path);
            var functionUi = ReadOptionalObject(fnElement, "ui", path);

            var parameters = new List<FunctionParamSchema>();
            if (fnElement.TryGetProperty("params", out var paramsElement))
            {
                if (paramsElement.ValueKind != JsonValueKind.Array)
                {
                    throw new YasnException("Поле schema.params должно быть массивом", path: path);
                }

                foreach (var paramElement in paramsElement.EnumerateArray())
                {
                    if (paramElement.ValueKind != JsonValueKind.Object)
                    {
                        throw new YasnException("Элемент schema.params должен быть объектом", path: path);
                    }

                    parameters.Add(new FunctionParamSchema(
                        RequireString(paramElement, "name", path),
                        TryReadString(paramElement, "type", path) ?? "Любой",
                        ReadOptionalSchemaTypeNode(paramElement, "typeNode", path),
                        ReadOptionalObject(paramElement, "ui", path)));
                }
            }

            result.Add(new FunctionSchema(
                name,
                parameters,
                returnType,
                isAsync,
                isPublicApi,
                returnTypeNode,
                functionUi));
        }

        return result;
    }

    private static string? TryReadString(JsonElement root, string propertyName, string? path)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new YasnException($"Поле метаданных '{propertyName}' должно быть строкой", path: path);
        }

        return element.GetString();
    }

    private static bool ReadOptionalBool(JsonElement root, string propertyName, bool defaultValue, string? path)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return defaultValue;
        }

        if (element.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        throw new YasnException($"Поле метаданных '{propertyName}' должно быть логическим значением", path: path);
    }

    private static SchemaTypeNode? ReadOptionalSchemaTypeNode(JsonElement root, string propertyName, string? path)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ReadSchemaTypeNode(element, path);
    }

    private static SchemaTypeNode ReadSchemaTypeNode(JsonElement element, string? path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new YasnException("Поле schema.typeNode должно быть объектом", path: path);
        }

        var kind = RequireString(element, "kind", path);
        return kind switch
        {
            "primitive" => SchemaTypeNode.Primitive(TryReadString(element, "name", path) ?? "Любой"),
            "list" => SchemaTypeNode.List(ReadSchemaTypeNode(RequireProperty(element, "element", path), path)),
            "dict" => SchemaTypeNode.Dict(
                ReadSchemaTypeNode(RequireProperty(element, "key", path), path),
                ReadSchemaTypeNode(RequireProperty(element, "value", path), path)),
            "union" => SchemaTypeNode.Union(ReadSchemaTypeVariants(element, path)),
            _ => throw new YasnException($"Неизвестный kind typeNode: {kind}", path: path),
        };
    }

    private static List<SchemaTypeNode> ReadSchemaTypeVariants(JsonElement root, string? path)
    {
        if (!root.TryGetProperty("variants", out var variantsElement) || variantsElement.ValueKind != JsonValueKind.Array)
        {
            throw new YasnException("Поле schema.typeNode.variants должно быть массивом", path: path);
        }

        var variants = variantsElement
            .EnumerateArray()
            .Select(item => ReadSchemaTypeNode(item, path))
            .ToList();

        if (variants.Count == 0)
        {
            throw new YasnException("Поле schema.typeNode.variants не должно быть пустым", path: path);
        }

        return variants;
    }

    private static Dictionary<string, object?>? ReadOptionalObject(JsonElement root, string propertyName, string? path)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new YasnException($"Поле метаданных '{propertyName}' должно быть объектом", path: path);
        }

        var decoded = BytecodeCodec.DecodeJsonValue(element);
        if (decoded is not Dictionary<object, object?> dict)
        {
            return null;
        }

        return ConvertToStringDictionary(dict);
    }

    private static Dictionary<string, object?> ConvertToStringDictionary(Dictionary<object, object?> source)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in source)
        {
            var key = pair.Key?.ToString() ?? string.Empty;
            result[key] = NormalizeDecodedValue(pair.Value);
        }

        return result;
    }

    private static object? NormalizeDecodedValue(object? value)
    {
        return value switch
        {
            Dictionary<object, object?> dict => ConvertToStringDictionary(dict),
            List<object?> list => list.Select(NormalizeDecodedValue).ToList(),
            _ => value,
        };
    }

    private static JsonElement RequireProperty(JsonElement root, string propertyName, string? path)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            throw new YasnException($"Поле метаданных '{propertyName}' отсутствует", path: path);
        }

        return element;
    }

    private static string RequireString(JsonElement root, string propertyName, string? path)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
        {
            throw new YasnException($"Поле метаданных '{propertyName}' должно быть строкой", path: path);
        }

        return element.GetString() ?? string.Empty;
    }
}
