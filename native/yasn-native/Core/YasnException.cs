using System.Globalization;

namespace YasnNative.Core;

public sealed class YasnException : Exception
{
    public int? Line { get; }
    public int? Col { get; }
    public string? PathHint { get; }

    public YasnException(string message, int? line = null, int? col = null, string? path = null)
        : base(BuildMessage(message, line, col, path))
    {
        Line = line;
        Col = col;
        PathHint = path;
    }

    public static YasnException At(string message, int line, int col, string? path = null)
    {
        return new YasnException(message, line, col, path);
    }

    private static string BuildMessage(string message, int? line, int? col, string? path)
    {
        if (line is null || col is null)
        {
            return string.IsNullOrWhiteSpace(path) ? message : $"{path}: {message}";
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"строка {line.Value}, столбец {col.Value}: {message}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{path}:{line.Value}:{col.Value}: {message}");
    }
}
