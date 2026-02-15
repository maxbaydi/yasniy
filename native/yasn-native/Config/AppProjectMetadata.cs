using System.Globalization;
using System.Text.RegularExpressions;
using Tomlyn.Model;
using YasnNative.Core;

namespace YasnNative.Config;

public sealed record AppProjectMetadata(
    string? Name,
    string? DisplayName,
    string? Description,
    string? Version,
    string? Publisher,
    string? ConfigPath)
{
    private static readonly Regex SemVerRegex = new(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NumberRegex = new(
        @"^(0|[1-9]\d*)(\.\d+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static AppProjectMetadata Empty { get; } = new(null, null, null, null, null, null);

    public static AppProjectMetadata LoadForSource(string sourcePath)
    {
        var fullSourcePath = System.IO.Path.GetFullPath(sourcePath);
        var startDirectory = System.IO.Path.GetDirectoryName(fullSourcePath) ?? Directory.GetCurrentDirectory();
        var configPath = TomlUtil.FindConfig(startDirectory);
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return Empty;
        }

        var data = TomlUtil.ReadToml(configPath);
        var app = TomlUtil.GetTable(data, "app", configPath);
        var modules = TomlUtil.GetTable(data, "modules", configPath);

        return new AppProjectMetadata(
            ReadStringValue(data, app, modules, configPath, "name"),
            ReadStringValue(data, app, modules, configPath, "displayName", "display_name"),
            ReadStringValue(data, app, modules, configPath, "description"),
            ReadVersionValue(data, app, modules, configPath, "version"),
            ReadStringValue(data, app, modules, configPath, "publisher"),
            configPath);
    }

    public string ResolveTechnicalName(string fallback)
    {
        return Normalize(Name) ?? fallback;
    }

    public string ResolveDisplayName(string fallback)
    {
        return Normalize(DisplayName) ?? fallback;
    }

    private static string? ReadStringValue(TomlTable root, TomlTable app, TomlTable modules, string configPath, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryGetRawValue(app, key, out var rawApp) && !TryGetRawValue(root, key, out rawApp) && !TryGetRawValue(modules, key, out rawApp))
            {
                continue;
            }

            if (rawApp is null)
            {
                return null;
            }

            if (rawApp is not string rawString)
            {
                throw new YasnException($"Поле {key} должно быть строкой", path: configPath);
            }

            return Normalize(rawString);
        }

        return null;
    }

    private static string? ReadVersionValue(TomlTable root, TomlTable app, TomlTable modules, string configPath, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryGetRawValue(app, key, out var raw) && !TryGetRawValue(root, key, out raw) && !TryGetRawValue(modules, key, out raw))
            {
                continue;
            }

            if (raw is null)
            {
                return null;
            }

            var parsed = ParseVersion(raw, key, configPath);
            ValidateVersion(parsed, key, configPath);
            return parsed;
        }

        return null;
    }

    private static string ParseVersion(object raw, string key, string configPath)
    {
        switch (raw)
        {
            case string s:
            {
                var normalized = Normalize(s);
                if (normalized is null)
                {
                    throw new YasnException($"Поле {key} не может быть пустым", path: configPath);
                }

                return normalized;
            }
            case long i64:
                return i64.ToString(CultureInfo.InvariantCulture);
            case int i32:
                return i32.ToString(CultureInfo.InvariantCulture);
            case double f64:
                return f64.ToString("0.###############################", CultureInfo.InvariantCulture);
            case float f32:
                return f32.ToString("0.###############################", CultureInfo.InvariantCulture);
            case decimal dec:
                return dec.ToString(CultureInfo.InvariantCulture);
            default:
                throw new YasnException(
                    $"Поле {key} должно быть числом или строкой semver (пример: 1 или \"1.2.3\")",
                    path: configPath);
        }
    }

    private static void ValidateVersion(string value, string key, string configPath)
    {
        if (NumberRegex.IsMatch(value) || SemVerRegex.IsMatch(value))
        {
            return;
        }

        throw new YasnException(
            $"Некорректное значение {key}: '{value}'. Используйте число (1, 1.2) или semver-строку (\"1.2.3\").",
            path: configPath);
    }

    private static bool TryGetRawValue(TomlTable table, string key, out object? raw)
    {
        if (table.TryGetValue(key, out raw))
        {
            return true;
        }

        var underscore = key.Replace("Name", "_name", StringComparison.Ordinal);
        return underscore != key && table.TryGetValue(underscore, out raw);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
