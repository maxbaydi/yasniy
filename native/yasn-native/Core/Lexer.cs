using System.Text;

namespace YasnNative.Core;

public static class Lexer
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "функция",
        "вернуть",
        "если",
        "иначе",
        "пока",
        "для",
        "в",
        "и",
        "или",
        "не",
        "истина",
        "ложь",
        "пусто",
        "пусть",
        "подключить",
        "из",
        "как",
        "экспорт",
        "асинхронная",
        "прервать",
        "продолжить",
        "ждать",
    };

    private static readonly HashSet<string> TwoCharTokens = new(StringComparer.Ordinal)
    {
        "->",
        "==",
        "!=",
        "<=",
        ">=",
    };

    private static readonly HashSet<char> SingleCharTokens =
    [
        '(', ')', ':', ',', '[', ']', '{', '}', '+', '-', '*', '/', '%', '=', '<', '>', '.', '|', '?',
    ];

    private static readonly HashSet<char> OpeningBrackets = ['(', '[', '{'];

    private static readonly Dictionary<char, char> ClosingBrackets = new()
    {
        [')'] = '(',
        [']'] = '[',
        ['}'] = '{',
    };

    public static List<Token> Tokenize(string source, string? path = null)
    {
        var text = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (text.StartsWith('\uFEFF'))
        {
            text = text[1..];
        }

        var lines = text.Split('\n');
        var tokens = new List<Token>();
        var indentStack = new Stack<int>();
        indentStack.Push(0);
        var bracketStack = new Stack<(char Open, int Line, int Col)>();

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var lineNo = lineIndex + 1;
            var line = lines[lineIndex];
            var tabIdx = line.IndexOf('\t');
            if (tabIdx >= 0)
            {
                throw YasnException.At("Табуляция запрещена, используйте пробелы", lineNo, tabIdx + 1, path);
            }

            var spaceCount = 0;
            while (spaceCount < line.Length && line[spaceCount] == ' ')
            {
                spaceCount++;
            }

            var rest = line[spaceCount..];
            if (string.IsNullOrEmpty(rest) || rest.StartsWith('#'))
            {
                continue;
            }

            if (bracketStack.Count == 0)
            {
                if (spaceCount > indentStack.Peek())
                {
                    indentStack.Push(spaceCount);
                    tokens.Add(new Token("INDENT", null, lineNo, 1));
                }
                else if (spaceCount < indentStack.Peek())
                {
                    while (spaceCount < indentStack.Peek())
                    {
                        indentStack.Pop();
                        tokens.Add(new Token("DEDENT", null, lineNo, 1));
                    }

                    if (spaceCount != indentStack.Peek())
                    {
                        throw YasnException.At("Некорректный уровень отступа", lineNo, 1, path);
                    }
                }
            }

            var idx = spaceCount;
            while (idx < line.Length)
            {
                var ch = line[idx];
                var col = idx + 1;

                if (ch == ' ')
                {
                    idx++;
                    continue;
                }

                if (ch == '#')
                {
                    break;
                }

                if (idx + 1 < line.Length)
                {
                    var pair = line.Substring(idx, 2);
                    if (TwoCharTokens.Contains(pair))
                    {
                        tokens.Add(new Token(pair, pair, lineNo, col));
                        idx += 2;
                        continue;
                    }
                }

                if (SingleCharTokens.Contains(ch))
                {
                    if (OpeningBrackets.Contains(ch))
                    {
                        bracketStack.Push((ch, lineNo, col));
                    }
                    else if (ClosingBrackets.TryGetValue(ch, out var expectedOpen))
                    {
                        if (bracketStack.Count == 0)
                        {
                            throw YasnException.At("Лишняя закрывающая скобка", lineNo, col, path);
                        }

                        var top = bracketStack.Pop();
                        if (top.Open != expectedOpen)
                        {
                            throw YasnException.At(
                                $"Несоответствующая скобка: '{top.Open}' открыта здесь",
                                top.Line,
                                top.Col,
                                path);
                        }
                    }

                    var tokenKind = ch.ToString();
                    tokens.Add(new Token(tokenKind, tokenKind, lineNo, col));
                    idx++;
                    continue;
                }

                if (ch == '"')
                {
                    idx++;
                    var builder = new StringBuilder();
                    var escaped = false;
                    while (idx < line.Length)
                    {
                        var current = line[idx];
                        if (escaped)
                        {
                            builder.Append(current switch
                            {
                                'n' => '\n',
                                't' => '\t',
                                'r' => '\r',
                                '"' => '"',
                                '\\' => '\\',
                                _ => throw YasnException.At($"Неизвестная escape-последовательность: \\{current}", lineNo, idx + 1, path),
                            });
                            escaped = false;
                            idx++;
                            continue;
                        }

                        if (current == '\\')
                        {
                            escaped = true;
                            idx++;
                            continue;
                        }

                        if (current == '"')
                        {
                            idx++;
                            tokens.Add(new Token("STRING", builder.ToString(), lineNo, col));
                            break;
                        }

                        builder.Append(current);
                        idx++;
                    }

                    if (idx >= line.Length && (tokens.Count == 0 || tokens[^1].Kind != "STRING" || tokens[^1].Line != lineNo || tokens[^1].Col != col))
                    {
                        throw YasnException.At("Незакрытая строка", lineNo, col, path);
                    }

                    continue;
                }

                if (char.IsDigit(ch))
                {
                    var start = idx;
                    while (idx < line.Length && char.IsDigit(line[idx]))
                    {
                        idx++;
                    }

                    var isFloat = false;
                    if (idx < line.Length && line[idx] == '.')
                    {
                        if (idx + 1 < line.Length && char.IsDigit(line[idx + 1]))
                        {
                            isFloat = true;
                            idx++;
                            while (idx < line.Length && char.IsDigit(line[idx]))
                            {
                                idx++;
                            }
                        }
                        else
                        {
                            throw YasnException.At("Ожидалась цифра после точки в числе", lineNo, idx + 1, path);
                        }
                    }

                    var raw = line[start..idx];
                    if (isFloat)
                    {
                        tokens.Add(new Token("FLOAT", double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture), lineNo, start + 1));
                    }
                    else
                    {
                        tokens.Add(new Token("INT", long.Parse(raw, System.Globalization.CultureInfo.InvariantCulture), lineNo, start + 1));
                    }

                    continue;
                }

                if (IsIdentStart(ch))
                {
                    var start = idx;
                    idx++;
                    while (idx < line.Length && IsIdentPart(line[idx]))
                    {
                        idx++;
                    }

                    var ident = line[start..idx];
                    if (Keywords.Contains(ident))
                    {
                        tokens.Add(new Token(ident, ident, lineNo, start + 1));
                    }
                    else
                    {
                        tokens.Add(new Token("IDENT", ident, lineNo, start + 1));
                    }

                    continue;
                }

                throw YasnException.At($"Неизвестный символ: {ch}", lineNo, col, path);
            }

            if (bracketStack.Count == 0)
            {
                tokens.Add(new Token("NEWLINE", null, lineNo, line.Length + 1));
            }
        }

        if (bracketStack.Count > 0)
        {
            var top = bracketStack.Peek();
            throw YasnException.At($"Незакрытая скобка: '{top.Open}'", top.Line, top.Col, path);
        }

        var eofLine = lines.Length + 1;
        if (tokens.Count > 0 && tokens[^1].Kind != "NEWLINE")
        {
            tokens.Add(new Token("NEWLINE", null, eofLine, 1));
        }

        while (indentStack.Count > 1)
        {
            indentStack.Pop();
            tokens.Add(new Token("DEDENT", null, eofLine, 1));
        }

        tokens.Add(new Token("EOF", null, eofLine, 1));
        return tokens;
    }

    private static bool IsIdentStart(char ch)
    {
        return ch == '_' || char.IsLetter(ch);
    }

    private static bool IsIdentPart(char ch)
    {
        return IsIdentStart(ch) || char.IsDigit(ch);
    }
}
