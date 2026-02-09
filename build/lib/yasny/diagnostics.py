from __future__ import annotations

from dataclasses import dataclass


@dataclass(slots=True)
class YasnyError(Exception):
    message: str
    line: int | None = None
    col: int | None = None
    path: str | None = None

    def __str__(self) -> str:
        location = ""
        if self.path:
            location += self.path
        if self.line is not None:
            location += f":{self.line}"
            if self.col is not None:
                location += f":{self.col}"
        if location:
            return f"{location}: ошибка: {self.message}"
        return f"ошибка: {self.message}"
