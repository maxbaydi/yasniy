using System.Buffers.Binary;
using System.Text.Json;
using YasnNative.Core;

namespace YasnNative.Bytecode;

public sealed record AppBundle(string Name, int Version, byte[] Bytecode);

public static class AppBundleCodec
{
    private static readonly byte[] AppMagic = "YASNYAP1"u8.ToArray();

    public const int AppVersion = 1;

    public static byte[] CreateBundle(string name, byte[] bytecode)
    {
        var meta = JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, object?>
            {
                ["name"] = name,
                ["version"] = AppVersion,
            },
            new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = false,
            });

        var output = new byte[AppMagic.Length + 4 + meta.Length + 4 + bytecode.Length];
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
        }
        catch (Exception ex)
        {
            throw new YasnException($"Не удалось разобрать метаданные приложения: {ex.Message}", path: path);
        }

        if (version != AppVersion)
        {
            throw new YasnException(
                $"Неподдерживаемая версия формата приложения: {version}, ожидается {AppVersion}",
                path: path);
        }

        var bytecodeLength = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
        offset += 4;
        if (offset + bytecodeLength != blob.Length)
        {
            throw new YasnException("Некорректная длина байткода в приложении", path: path);
        }

        var bytecode = blob.AsSpan(offset, checked((int)bytecodeLength)).ToArray();
        return new AppBundle(name, version, bytecode);
    }

    public static (AppBundle Bundle, ProgramBC Program) DecodeBundleToProgram(byte[] blob, string? path = null)
    {
        var bundle = ReadBundle(blob, path);
        var program = BytecodeCodec.DecodeProgram(bundle.Bytecode, path);
        return (bundle, program);
    }
}
