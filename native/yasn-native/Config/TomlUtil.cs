using Tomlyn;
using Tomlyn.Model;
using YasnNative.Core;

namespace YasnNative.Config;

public static class TomlUtil
{
    public static TomlTable ReadToml(string path)
    {
        var text = string.Empty;
        try
        {
            text = File.ReadAllText(path);
            var model = Toml.ToModel(text);
            return (TomlTable)model;
        }
        catch (Exception ex)
        {
            if (LooksLikeUnquotedSemVer(text))
            {
                throw new YasnException(
                    "Поле version в yasn.toml не может быть в формате 0.1.1 без кавычек. Используйте число (version = 1) или semver-строку (version = \"0.1.1\").",
                    path: path);
            }

            throw new YasnException($"Не удалось прочитать {System.IO.Path.GetFileName(path)}: {ex.Message}", path: path);
        }
    }

    public static string? FindConfig(string startDirectory)
    {
        var current = new DirectoryInfo(System.IO.Path.GetFullPath(startDirectory));
        while (current is not null)
        {
            var candidate = System.IO.Path.Combine(current.FullName, "yasn.toml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    public static string FindProjectRoot(string startDirectory)
    {
        var current = new DirectoryInfo(System.IO.Path.GetFullPath(startDirectory));
        while (current is not null)
        {
            var yasnToml = System.IO.Path.Combine(current.FullName, "yasn.toml");
            if (File.Exists(yasnToml))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return System.IO.Path.GetFullPath(startDirectory);
    }

    public static TomlTable GetTable(TomlTable table, string key, string? pathForError = null)
    {
        if (!table.TryGetValue(key, out var raw) || raw is null)
        {
            return new TomlTable();
        }

        if (raw is TomlTable sub)
        {
            return sub;
        }

        throw new YasnException($"Секция [{key}] должна быть объектом", path: pathForError);
    }

    public static string? GetString(TomlTable table, string key, string? pathForError = null)
    {
        if (!table.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        if (raw is string s)
        {
            return s;
        }

        throw new YasnException($"Поле {key} должно быть строкой", path: pathForError);
    }

    public static int? GetInt(TomlTable table, string key, string? pathForError = null)
    {
        if (!table.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        if (raw is long i64)
        {
            return checked((int)i64);
        }

        if (raw is int i32)
        {
            return i32;
        }

        throw new YasnException($"Поле {key} должно быть целым числом", path: pathForError);
    }

    public static List<string> GetStringList(TomlTable table, string key, string? pathForError = null)
    {
        if (!table.TryGetValue(key, out var raw) || raw is null)
        {
            return [];
        }

        if (raw is TomlArray arr)
        {
            var result = new List<string>(arr.Count);
            foreach (var item in arr)
            {
                if (item is string s)
                {
                    result.Add(s);
                }
                else
                {
                    throw new YasnException($"Поле {key} должно быть списком строк", path: pathForError);
                }
            }

            return result;
        }

        throw new YasnException($"Поле {key} должно быть списком строк", path: pathForError);
    }

    private static bool LooksLikeUnquotedSemVer(string text)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(
            text,
            @"(?m)^\s*version\s*=\s*\d+\.\d+\.\d+\s*(?:#.*)?$");
    }
}
