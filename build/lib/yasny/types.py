from __future__ import annotations

from dataclasses import dataclass

from . import ast
from .diagnostics import YasnyError


@dataclass(frozen=True, slots=True)
class Type:
    name: str
    args: tuple["Type", ...] = ()

    def __str__(self) -> str:
        if self.name == "Объединение":
            return " | ".join(str(x) for x in self.args)
        if not self.args:
            return self.name
        if self.name == "Список":
            return f"Список[{self.args[0]}]"
        if self.name == "Словарь":
            return f"Словарь[{self.args[0]},{self.args[1]}]"
        joined = ",".join(str(x) for x in self.args)
        return f"{self.name}[{joined}]"


INT = Type("Цел")
FLOAT = Type("Дроб")
BOOL = Type("Лог")
STRING = Type("Строка")
VOID = Type("Пусто")
UNION = "Объединение"


def list_of(element: Type) -> Type:
    return Type("Список", (element,))


def dict_of(key: Type, value: Type) -> Type:
    return Type("Словарь", (key, value))


def union_of(*variants: Type) -> Type:
    flat: list[Type] = []
    for variant in variants:
        if variant.name == UNION:
            flat.extend(list(variant.args))
        else:
            flat.append(variant)

    uniq: list[Type] = []
    for t in flat:
        if t not in uniq:
            uniq.append(t)
    if not uniq:
        return VOID
    if len(uniq) == 1:
        return uniq[0]
    return Type(UNION, tuple(uniq))


def is_union(t: Type) -> bool:
    return t.name == UNION


def variants_of(t: Type) -> tuple[Type, ...]:
    if is_union(t):
        return t.args
    return (t,)


def is_numeric(t: Type) -> bool:
    return t in (INT, FLOAT)


def is_numeric_like(t: Type) -> bool:
    return all(is_numeric(v) for v in variants_of(t))


def contains_void(t: Type) -> bool:
    return VOID in variants_of(t)


def without_void(t: Type) -> Type:
    vals = [x for x in variants_of(t) if x != VOID]
    if not vals:
        return VOID
    return union_of(*vals)


def overlaps(a: Type, b: Type) -> bool:
    set_a = set(variants_of(a))
    set_b = set(variants_of(b))
    return not set_a.isdisjoint(set_b)


def from_type_node(node: ast.TypeNode, path: str | None = None) -> Type:
    if isinstance(node, ast.PrimitiveTypeNode):
        mapping = {
            "Цел": INT,
            "Дроб": FLOAT,
            "Лог": BOOL,
            "Строка": STRING,
            "Пусто": VOID,
        }
        if node.name not in mapping:
            raise YasnyError(f"Неизвестный тип: {node.name}", node.line, node.col, path)
        return mapping[node.name]
    if isinstance(node, ast.ListTypeNode):
        return list_of(from_type_node(node.element, path))
    if isinstance(node, ast.DictTypeNode):
        return dict_of(from_type_node(node.key, path), from_type_node(node.value, path))
    if isinstance(node, ast.UnionTypeNode):
        return union_of(*[from_type_node(v, path) for v in node.variants])
    raise YasnyError("Неизвестный узел типа", node.line, node.col, path)


@dataclass(slots=True)
class FunctionSignature:
    name: str
    params: list[Type]
    return_type: Type
    builtin: bool = False
    varargs: bool = False


def is_assignable(expected: Type, actual: Type) -> bool:
    exp_variants = variants_of(expected)
    act_variants = variants_of(actual)
    for act in act_variants:
        if act not in exp_variants:
            return False
    return True
