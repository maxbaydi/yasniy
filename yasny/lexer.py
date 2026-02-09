from __future__ import annotations

from dataclasses import dataclass

from .diagnostics import YasnyError


KEYWORDS = {
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
    "прервать",
    "продолжить",
}

TWO_CHAR_TOKENS = {"->", "==", "!=", "<=", ">="}
SINGLE_CHAR_TOKENS = set("():,[]{}+-*/%=<>.|?")


@dataclass(slots=True)
class Token:
    kind: str
    value: object | None
    line: int
    col: int


def _is_ident_start(ch: str) -> bool:
    return ch == "_" or ch.isalpha()


def _is_ident_part(ch: str) -> bool:
    return _is_ident_start(ch) or ch.isdigit()


def tokenize(source: str, path: str | None = None) -> list[Token]:
    text = source.replace("\r\n", "\n").replace("\r", "\n")
    if text.startswith("\ufeff"):
        text = text[1:]
    lines = text.split("\n")
    tokens: list[Token] = []
    indent_stack = [0]

    for line_no, line in enumerate(lines, start=1):
        tab_idx = line.find("\t")
        if tab_idx != -1:
            raise YasnyError("Табуляция запрещена, используйте пробелы", line_no, tab_idx + 1, path)

        space_count = 0
        while space_count < len(line) and line[space_count] == " ":
            space_count += 1

        rest = line[space_count:]
        if rest == "" or rest.startswith("#"):
            continue

        if space_count > indent_stack[-1]:
            indent_stack.append(space_count)
            tokens.append(Token("INDENT", None, line_no, 1))
        elif space_count < indent_stack[-1]:
            while space_count < indent_stack[-1]:
                indent_stack.pop()
                tokens.append(Token("DEDENT", None, line_no, 1))
            if space_count != indent_stack[-1]:
                raise YasnyError("Некорректный уровень отступа", line_no, 1, path)

        idx = space_count
        while idx < len(line):
            ch = line[idx]
            col = idx + 1

            if ch == " ":
                idx += 1
                continue
            if ch == "#":
                break

            pair = line[idx : idx + 2]
            if pair in TWO_CHAR_TOKENS:
                tokens.append(Token(pair, pair, line_no, col))
                idx += 2
                continue

            if ch in SINGLE_CHAR_TOKENS:
                tokens.append(Token(ch, ch, line_no, col))
                idx += 1
                continue

            if ch == '"':
                idx += 1
                chars: list[str] = []
                escaped = False
                while idx < len(line):
                    cur = line[idx]
                    if escaped:
                        if cur == "n":
                            chars.append("\n")
                        elif cur == "t":
                            chars.append("\t")
                        elif cur == "r":
                            chars.append("\r")
                        elif cur == '"':
                            chars.append('"')
                        elif cur == "\\":
                            chars.append("\\")
                        else:
                            raise YasnyError(f"Неизвестная escape-последовательность: \\{cur}", line_no, idx + 1, path)
                        escaped = False
                        idx += 1
                        continue
                    if cur == "\\":
                        escaped = True
                        idx += 1
                        continue
                    if cur == '"':
                        idx += 1
                        tokens.append(Token("STRING", "".join(chars), line_no, col))
                        break
                    chars.append(cur)
                    idx += 1
                else:
                    raise YasnyError("Незакрытая строка", line_no, col, path)
                continue

            if ch.isdigit():
                start = idx
                while idx < len(line) and line[idx].isdigit():
                    idx += 1
                is_float = False
                if idx < len(line) and line[idx] == ".":
                    if idx + 1 < len(line) and line[idx + 1].isdigit():
                        is_float = True
                        idx += 1
                        while idx < len(line) and line[idx].isdigit():
                            idx += 1
                    else:
                        raise YasnyError("Ожидалась цифра после точки в числе", line_no, idx + 1, path)
                raw = line[start:idx]
                if is_float:
                    tokens.append(Token("FLOAT", float(raw), line_no, start + 1))
                else:
                    tokens.append(Token("INT", int(raw), line_no, start + 1))
                continue

            if _is_ident_start(ch):
                start = idx
                idx += 1
                while idx < len(line) and _is_ident_part(line[idx]):
                    idx += 1
                ident = line[start:idx]
                if ident in KEYWORDS:
                    tokens.append(Token(ident, ident, line_no, start + 1))
                else:
                    tokens.append(Token("IDENT", ident, line_no, start + 1))
                continue

            raise YasnyError(f"Неизвестный символ: {ch}", line_no, col, path)

        tokens.append(Token("NEWLINE", None, line_no, len(line) + 1))

    eof_line = len(lines) + 1
    if tokens and tokens[-1].kind != "NEWLINE":
        tokens.append(Token("NEWLINE", None, eof_line, 1))

    while len(indent_stack) > 1:
        indent_stack.pop()
        tokens.append(Token("DEDENT", None, eof_line, 1))

    tokens.append(Token("EOF", None, eof_line, 1))
    return tokens
