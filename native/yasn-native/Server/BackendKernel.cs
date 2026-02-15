using YasnNative.Bytecode;
using YasnNative.Core;
using YasnNative.Runtime;

namespace YasnNative.Server;

public sealed class BackendKernel
{
    private readonly VirtualMachine _vm;
    private readonly List<FunctionSchema> _schema;

    private BackendKernel(string sourcePath, VirtualMachine vm, List<FunctionSchema> schema)
    {
        SourcePath = sourcePath;
        _vm = vm;
        _schema = schema
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToList();
    }

    public string SourcePath { get; }

    public static BackendKernel FromFile(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        var source = File.ReadAllText(fullPath);
        var loadedProgram = Pipeline.LoadProgram(source, fullPath);
        var program = Pipeline.CompileProgram(loadedProgram, fullPath);
        var vm = new VirtualMachine(program, fullPath);
        var schema = FunctionSchemaBuilder.FromProgramNode(loadedProgram);
        return new BackendKernel(fullPath, vm, schema);
    }

    public static BackendKernel FromProgram(ProgramBC program, string sourcePath = "<bundle>", IEnumerable<FunctionSchema>? schema = null)
    {
        var vm = new VirtualMachine(program, sourcePath);
        var effectiveSchema = schema?.ToList() ?? FunctionSchemaBuilder.FromProgramBytecode(program);
        return new BackendKernel(sourcePath, vm, effectiveSchema);
    }

    public List<string> ListFunctions()
    {
        return _schema.Select(item => item.Name).ToList();
    }

    public List<FunctionSchema> ListSchema()
    {
        return _schema
            .Select(item => item with { Params = [.. item.Params] })
            .ToList();
    }

    public object? Call(string functionName, List<object?>? args = null, bool resetState = true)
    {
        return _vm.CallFunction(functionName, args, resetState);
    }
}
