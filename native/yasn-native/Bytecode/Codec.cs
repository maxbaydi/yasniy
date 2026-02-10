using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using YasnNative.Core;

namespace YasnNative.Bytecode;

public static class BytecodeCodec
{
    private static readonly byte[] Magic = "YASNYBC1"u8.ToArray();

    public static byte[] EncodeProgram(ProgramBC program)
    {
        var payload = new Dictionary<string, object?>
        {
            ["functions"] = program.Functions.ToDictionary(
                pair => pair.Key,
                pair => (object?)EncodeFunction(pair.Value),
                StringComparer.Ordinal),
            ["entry"] = EncodeFunction(program.Entry),
            ["global_count"] = program.GlobalCount,
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions.Default);
        var output = new byte[Magic.Length + 4 + json.Length];
        Buffer.BlockCopy(Magic, 0, output, 0, Magic.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(Magic.Length, 4), (uint)json.Length);
        Buffer.BlockCopy(json, 0, output, Magic.Length + 4, json.Length);
        return output;
    }

    public static ProgramBC DecodeProgram(byte[] blob, string? path = null)
    {
        if (blob.Length < Magic.Length + 4)
        {
            throw new YasnException("Файл байткода слишком короткий", path: path);
        }

        if (!blob.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new YasnException("Неверная сигнатура файла .ybc", path: path);
        }

        var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(Magic.Length, 4));
        var payload = blob.AsSpan(Magic.Length + 4).ToArray();
        if (payloadLength != payload.Length)
        {
            throw new YasnException("Некорректная длина полезной нагрузки .ybc", path: path);
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            var functions = new Dictionary<string, FunctionBC>(StringComparer.Ordinal);
            foreach (var fn in root.GetProperty("functions").EnumerateObject())
            {
                functions[fn.Name] = DecodeFunction(fn.Value);
            }

            var entry = DecodeFunction(root.GetProperty("entry"));
            var globalCount = root.TryGetProperty("global_count", out var globalCountElement)
                ? ToInt(globalCountElement)
                : 0;

            return new ProgramBC
            {
                Functions = functions,
                Entry = entry,
                GlobalCount = globalCount,
            };
        }
        catch (YasnException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new YasnException($"Не удалось разобрать JSON байткода: {ex.Message}", path: path);
        }
    }

    public static object? DecodeJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var i) => i,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.Array => element.EnumerateArray().Select(DecodeJsonValue).ToList(),
            JsonValueKind.Object => element
                .EnumerateObject()
                .ToDictionary(
                    prop => (object)prop.Name,
                    prop => DecodeJsonValue(prop.Value),
                    ObjectKeyComparer.Instance),
            _ => null,
        };
    }

    public static object? NormalizeForJson(object? value)
    {
        return value switch
        {
            null => null,
            bool b => b,
            long i => i,
            int i => i,
            double d => d,
            float f => f,
            decimal d => d,
            string s => s,
            List<object?> list => list.Select(NormalizeForJson).ToList(),
            Dictionary<object, object?> dict => dict.ToDictionary(
                pair => ValueToString(pair.Key),
                pair => NormalizeForJson(pair.Value),
                StringComparer.Ordinal),
            _ => ValueToString(value),
        };
    }

    private static Dictionary<string, object?> EncodeFunction(FunctionBC fn)
    {
        return new Dictionary<string, object?>
        {
            ["name"] = fn.Name,
            ["params"] = fn.Params,
            ["local_count"] = fn.LocalCount,
            ["instructions"] = fn.Instructions.Select(ins => new Dictionary<string, object?>
            {
                ["op"] = ins.Op,
                ["args"] = ins.Args,
            }).ToList(),
        };
    }

    private static FunctionBC DecodeFunction(JsonElement element)
    {
        var instructions = new List<InstructionBC>();
        foreach (var ins in element.GetProperty("instructions").EnumerateArray())
        {
            var args = ins.TryGetProperty("args", out var argsElement)
                ? argsElement.EnumerateArray().Select(DecodeJsonValue).ToList()
                : [];
            instructions.Add(new InstructionBC
            {
                Op = ins.GetProperty("op").GetString() ?? string.Empty,
                Args = args,
            });
        }

        return new FunctionBC
        {
            Name = element.GetProperty("name").GetString() ?? string.Empty,
            Params = element.GetProperty("params").EnumerateArray().Select(v => v.GetString() ?? string.Empty).ToList(),
            LocalCount = ToInt(element.GetProperty("local_count")),
            Instructions = instructions,
        };
    }

    private static int ToInt(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var i32) => i32,
            JsonValueKind.Number when value.TryGetInt64(out var i64) => checked((int)i64),
            _ => throw new YasnException("Ожидалось целое число в байткоде"),
        };
    }

    private static string ValueToString(object? value)
    {
        return value switch
        {
            null => "пусто",
            bool b => b ? "истина" : "ложь",
            string s => s,
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
    }

    private sealed class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        };
    }

    private sealed class ObjectKeyComparer : IEqualityComparer<object>
    {
        public static readonly ObjectKeyComparer Instance = new();

        public new bool Equals(object? x, object? y)
        {
            return ReferenceEquals(x, y) || x?.Equals(y) == true;
        }

        public int GetHashCode(object obj)
        {
            return obj.GetHashCode();
        }
    }
}


