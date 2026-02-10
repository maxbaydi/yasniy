using YasnNative.Bytecode;
using YasnNative.Runtime;

namespace YasnNative.Core;

public static class Pipeline
{
    public static ProgramNode ParseSource(string source, string? path = null)
    {
        var tokens = Lexer.Tokenize(source, path);
        var parser = new Parser(tokens, path);
        return parser.Parse();
    }

    public static ProgramNode LoadProgram(string source, string? path = null)
    {
        var resolver = new ModuleResolver();
        return resolver.ResolveEntry(source, path);
    }

    public static ProgramBC CompileProgram(ProgramNode program, string? path = null)
    {
        TypeChecker.Check(program, path);
        var compiler = new Compiler(path);
        return compiler.Compile(program);
    }

    public static ProgramBC CompileSource(string source, string? path = null)
    {
        var program = LoadProgram(source, path);
        return CompileProgram(program, path);
    }

    public static void RunProgram(ProgramBC program, string? path = null)
    {
        var vm = new VirtualMachine(program, path);
        vm.Run();
    }
}
