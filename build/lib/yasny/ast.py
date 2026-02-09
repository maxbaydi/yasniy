from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any


@dataclass(slots=True)
class Node:
    line: int
    col: int


@dataclass(slots=True)
class TypeNode(Node):
    pass


@dataclass(slots=True)
class PrimitiveTypeNode(TypeNode):
    name: str


@dataclass(slots=True)
class ListTypeNode(TypeNode):
    element: TypeNode


@dataclass(slots=True)
class DictTypeNode(TypeNode):
    key: TypeNode
    value: TypeNode


@dataclass(slots=True)
class UnionTypeNode(TypeNode):
    variants: list[TypeNode]


@dataclass(slots=True)
class Param(Node):
    name: str
    type_node: TypeNode


@dataclass(slots=True)
class Stmt(Node):
    pass


@dataclass(slots=True)
class Expr(Node):
    inferred_type: Any | None = field(default=None, init=False, repr=False)


@dataclass(slots=True)
class Program(Node):
    statements: list[Stmt]


@dataclass(slots=True)
class VarDecl(Stmt):
    name: str
    annotation: TypeNode | None
    value: Expr
    exported: bool = False


@dataclass(slots=True)
class Assign(Stmt):
    name: str
    value: Expr


@dataclass(slots=True)
class IndexAssign(Stmt):
    target: Expr
    index: Expr
    value: Expr


@dataclass(slots=True)
class FuncDecl(Stmt):
    name: str
    params: list[Param]
    return_type: TypeNode
    body: list[Stmt]
    exported: bool = False


@dataclass(slots=True)
class IfStmt(Stmt):
    condition: Expr
    then_body: list[Stmt]
    else_body: list[Stmt] | None


@dataclass(slots=True)
class WhileStmt(Stmt):
    condition: Expr
    body: list[Stmt]


@dataclass(slots=True)
class ForStmt(Stmt):
    var_name: str
    iterable: Expr
    body: list[Stmt]


@dataclass(slots=True)
class ReturnStmt(Stmt):
    value: Expr


@dataclass(slots=True)
class ExprStmt(Stmt):
    expr: Expr


@dataclass(slots=True)
class ImportItem(Node):
    name: str
    alias: str | None


@dataclass(slots=True)
class ImportAll(Stmt):
    module_path: str
    alias: str | None = None


@dataclass(slots=True)
class ImportFrom(Stmt):
    module_path: str
    items: list[ImportItem]


@dataclass(slots=True)
class BreakStmt(Stmt):
    pass


@dataclass(slots=True)
class ContinueStmt(Stmt):
    pass


@dataclass(slots=True)
class Identifier(Expr):
    name: str


@dataclass(slots=True)
class Literal(Expr):
    value: Any
    kind: str


@dataclass(slots=True)
class ListLiteral(Expr):
    elements: list[Expr]


@dataclass(slots=True)
class DictLiteral(Expr):
    entries: list[tuple[Expr, Expr]]


@dataclass(slots=True)
class IndexExpr(Expr):
    target: Expr
    index: Expr


@dataclass(slots=True)
class MemberExpr(Expr):
    target: Expr
    member: str


@dataclass(slots=True)
class UnaryOp(Expr):
    op: str
    operand: Expr


@dataclass(slots=True)
class BinaryOp(Expr):
    left: Expr
    op: str
    right: Expr


@dataclass(slots=True)
class Call(Expr):
    callee: Expr
    args: list[Expr]
