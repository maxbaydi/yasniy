using YasnNative.Core;
using YasnNative.Runtime;

namespace YasnNative.Server;

public sealed class BackendKernel
{
    private readonly VirtualMachine _vm;

    private BackendKernel(string sourcePath, VirtualMachine vm)
    {
        SourcePath = sourcePath;
        _vm = vm;
    }

    public string SourcePath { get; }

    public static BackendKernel FromFile(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        var source = File.ReadAllText(fullPath);
        var program = Pipeline.CompileSource(source, fullPath);
        var vm = new VirtualMachine(program, fullPath);
        return new BackendKernel(fullPath, vm);
    }

    public List<string> ListFunctions()
    {
        return _vm.Program.Functions.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();
    }

    public object? Call(string functionName, List<object?>? args = null, bool resetState = true)
    {
        return _vm.CallFunction(functionName, args, resetState);
    }
}
