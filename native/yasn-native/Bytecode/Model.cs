namespace YasnNative.Bytecode;

public sealed class InstructionBC
{
    public required string Op { get; init; }

    public List<object?> Args { get; init; } = [];
}

public sealed class FunctionBC
{
    public required string Name { get; init; }

    public List<string> Params { get; init; } = [];

    public int LocalCount { get; init; }

    public List<InstructionBC> Instructions { get; init; } = [];
}

public sealed class ProgramBC
{
    public Dictionary<string, FunctionBC> Functions { get; init; } = new(StringComparer.Ordinal);

    public required FunctionBC Entry { get; init; }

    public int GlobalCount { get; init; }
}
