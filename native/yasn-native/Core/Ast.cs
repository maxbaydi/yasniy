namespace YasnNative.Core;

public abstract record Node(int Line, int Col);

public abstract record TypeNode(int Line, int Col) : Node(Line, Col);

public sealed record PrimitiveTypeNode(int Line, int Col, string Name) : TypeNode(Line, Col);

public sealed record ListTypeNode(int Line, int Col, TypeNode Element) : TypeNode(Line, Col);

public sealed record DictTypeNode(int Line, int Col, TypeNode Key, TypeNode Value) : TypeNode(Line, Col);

public sealed record UnionTypeNode(int Line, int Col, List<TypeNode> Variants) : TypeNode(Line, Col);

public sealed record ParamNode(int Line, int Col, string Name, TypeNode TypeNode) : Node(Line, Col);

public abstract record Stmt(int Line, int Col) : Node(Line, Col);

public abstract record Expr(int Line, int Col) : Node(Line, Col);

public sealed record ProgramNode(int Line, int Col, List<Stmt> Statements) : Node(Line, Col);

public sealed record VarDeclStmt(
    int Line,
    int Col,
    string Name,
    TypeNode? Annotation,
    Expr Value,
    bool Exported = false) : Stmt(Line, Col);

public sealed record AssignStmt(int Line, int Col, string Name, Expr Value) : Stmt(Line, Col);

public sealed record IndexAssignStmt(int Line, int Col, Expr Target, Expr Index, Expr Value) : Stmt(Line, Col);

public sealed record FuncDeclStmt(
    int Line,
    int Col,
    string Name,
    List<ParamNode> Params,
    TypeNode ReturnType,
    List<Stmt> Body,
    bool Exported = false,
    bool IsAsync = false) : Stmt(Line, Col);

public sealed record IfStmt(int Line, int Col, Expr Condition, List<Stmt> ThenBody, List<Stmt>? ElseBody) : Stmt(Line, Col);

public sealed record WhileStmt(int Line, int Col, Expr Condition, List<Stmt> Body) : Stmt(Line, Col);

public sealed record ForStmt(int Line, int Col, string VarName, Expr Iterable, List<Stmt> Body) : Stmt(Line, Col);

public sealed record ReturnStmt(int Line, int Col, Expr Value) : Stmt(Line, Col);

public sealed record ExprStmt(int Line, int Col, Expr Expr) : Stmt(Line, Col);

public sealed record ImportItemNode(int Line, int Col, string Name, string? Alias) : Node(Line, Col);

public sealed record ImportAllStmt(int Line, int Col, string ModulePath, string? Alias = null) : Stmt(Line, Col);

public sealed record ImportFromStmt(int Line, int Col, string ModulePath, List<ImportItemNode> Items) : Stmt(Line, Col);

public sealed record BreakStmt(int Line, int Col) : Stmt(Line, Col);

public sealed record ContinueStmt(int Line, int Col) : Stmt(Line, Col);

public sealed record IdentifierExpr(int Line, int Col, string Name) : Expr(Line, Col);

public sealed record LiteralExpr(int Line, int Col, object? Value, string Kind) : Expr(Line, Col);

public sealed record ListLiteralExpr(int Line, int Col, List<Expr> Elements) : Expr(Line, Col);

public sealed record DictLiteralExpr(int Line, int Col, List<(Expr Key, Expr Value)> Entries) : Expr(Line, Col);

public sealed record IndexExpr(int Line, int Col, Expr Target, Expr Index) : Expr(Line, Col);

public sealed record MemberExpr(int Line, int Col, Expr Target, string Member) : Expr(Line, Col);

public sealed record UnaryExpr(int Line, int Col, string Op, Expr Operand) : Expr(Line, Col);

public sealed record AwaitExpr(int Line, int Col, Expr Operand) : Expr(Line, Col);

public sealed record BinaryExpr(int Line, int Col, Expr Left, string Op, Expr Right) : Expr(Line, Col);

public sealed record CallExpr(int Line, int Col, Expr Callee, List<Expr> Args) : Expr(Line, Col);

public readonly record struct Token(string Kind, object? Value, int Line, int Col);
