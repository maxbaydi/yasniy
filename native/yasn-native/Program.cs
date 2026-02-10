using System.Text.RegularExpressions;
using YasnNative.App;
using YasnNative.Bytecode;
using YasnNative.Core;
using YasnNative.Deps;
using YasnNative.Runner;
using YasnNative.Runtime;
using YasnNative.Server;

namespace YasnNative;

internal static class Program
{
    private static readonly HashSet<string> ModeNames = new(StringComparer.Ordinal) { "dev", "start" };

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 2;
        }

        try
        {
            var command = args[0];
            return command switch
            {
                "run" => CommandRun(args),
                "dev" => CommandMode("dev", args),
                "start" => CommandMode("start", args),
                "serve" => CommandServe(args),
                "check" => CommandCheck(args),
                "test" => CommandTest(args),
                "build" => CommandBuild(args),
                "exec" => CommandExec(args),
                "pack" => CommandPack(args),
                "run-app" => CommandRunApp(args),
                "install-app" => CommandInstallApp(args),
                "paths" => CommandPaths(args),
                "deps" => CommandDeps(args),
                "help" or "--help" or "-h" => HelpAndReturn(),
                "version" or "--version" or "-v" => VersionAndReturn(),
                _ => UnknownCommand(command),
            };
        }
        catch (YasnException ex)
        {
            Console.WriteLine(ex.Message);
            return 1;
        }
        catch (FileNotFoundException ex)
        {
            Console.WriteLine($"ошибка: файл не найден: {ex.FileName}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ошибка выполнения: {ex.Message}");
            return 1;
        }
    }

    private static int CommandRun(string[] args)
    {
        var target = RequireArg(args, 1, "run требует путь к .яс файлу или режим dev/start");
        var backend = ParseOption(args, "--backend");
        var host = ParseOption(args, "--host");
        var portRaw = ParseOption(args, "--port");
        var port = ParseNullableInt(portRaw, "Некорректный порт");

        if (ModeNames.Contains(target))
        {
            return ProjectRunner.RunMode(target, backend, host, port);
        }

        var fullPath = Path.GetFullPath(target);
        var source = File.ReadAllText(fullPath);
        var program = Pipeline.CompileSource(source, fullPath);
        var vm = new VirtualMachine(program, fullPath);
        vm.Run();
        return 0;
    }

    private static int CommandMode(string mode, string[] args)
    {
        var backend = ParseOption(args, "--backend");
        var host = ParseOption(args, "--host");
        var portRaw = ParseOption(args, "--port");
        var port = ParseNullableInt(portRaw, "Некорректный порт");
        return ProjectRunner.RunMode(mode, backend, host, port);
    }

    private static int CommandServe(string[] args)
    {
        var sourcePath = RequireArg(args, 1, "serve требует путь к backend .яс файлу");
        var host = ParseOption(args, "--host") ?? "127.0.0.1";
        var portRaw = ParseOption(args, "--port") ?? "8000";
        if (!int.TryParse(portRaw, out var port))
        {
            throw new YasnException($"Некорректный порт: {portRaw}");
        }

        BackendServer.Serve(sourcePath, host, port);
        return 0;
    }

    private static int CommandCheck(string[] args)
    {
        var sourcePath = RequireArg(args, 1, "check требует путь к .яс файлу");
        var fullPath = Path.GetFullPath(sourcePath);
        var source = File.ReadAllText(fullPath);
        _ = Pipeline.CompileSource(source, fullPath);
        Console.WriteLine("Проверка пройдена: ошибок не найдено.");
        return 0;
    }

    private static int CommandTest(string[] args)
    {
        string? target = null;
        if (args.Length > 1 && !args[1].StartsWith("-", StringComparison.Ordinal))
        {
            target = args[1];
        }

        var pattern = ParseOption(args, "--pattern");
        var failFast = HasFlag(args, "--fail-fast");
        var verbose = HasFlag(args, "--verbose");
        return TestRunner.Run(target, pattern, failFast, verbose);
    }

    private static int CommandBuild(string[] args)
    {
        var sourcePath = RequireArg(args, 1, "build требует путь к .яс файлу");
        var outputPath = ParseOption(args, "-o") ?? ParseOption(args, "--output");

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var source = File.ReadAllText(fullSourcePath);
        var program = Pipeline.CompileSource(source, fullSourcePath);
        var output = outputPath is null
            ? Path.ChangeExtension(fullSourcePath, ".ybc")
            : Path.GetFullPath(outputPath);

        File.WriteAllBytes(output, BytecodeCodec.EncodeProgram(program));
        Console.WriteLine($"Байткод сохранён: {output}");
        return 0;
    }

    private static int CommandExec(string[] args)
    {
        var bytecodePath = RequireArg(args, 1, "exec требует путь к .ybc файлу");
        var fullPath = Path.GetFullPath(bytecodePath);
        var program = BytecodeCodec.DecodeProgram(File.ReadAllBytes(fullPath), fullPath);
        var vm = new VirtualMachine(program, fullPath);
        vm.Run();
        return 0;
    }

    private static int CommandPack(string[] args)
    {
        var sourcePath = RequireArg(args, 1, "pack требует путь к .яс файлу");
        var outputPath = ParseOption(args, "-o") ?? ParseOption(args, "--output");
        var appName = ParseOption(args, "--name") ?? Path.GetFileNameWithoutExtension(sourcePath);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var source = File.ReadAllText(fullSourcePath);
        var program = Pipeline.CompileSource(source, fullSourcePath);
        var bytecode = BytecodeCodec.EncodeProgram(program);
        var bundle = AppBundleCodec.CreateBundle(appName, bytecode);

        var output = outputPath is null
            ? Path.ChangeExtension(fullSourcePath, ".yapp")
            : Path.GetFullPath(outputPath);
        File.WriteAllBytes(output, bundle);
        Console.WriteLine($"Приложение упаковано: {output}");
        return 0;
    }

    private static int CommandRunApp(string[] args)
    {
        var appPath = RequireArg(args, 1, "run-app требует путь к .yapp файлу");
        var fullPath = Path.GetFullPath(appPath);
        var (bundle, program) = AppBundleCodec.DecodeBundleToProgram(File.ReadAllBytes(fullPath), fullPath);
        var vm = new VirtualMachine(program, fullPath);
        vm.Run();
        Console.WriteLine($"[yasn] Приложение '{bundle.Name}' завершено.");
        return 0;
    }

    private static int CommandInstallApp(string[] args)
    {
        var sourcePath = RequireArg(args, 1, "install-app требует путь к .яс файлу");
        var nameRaw = ParseOption(args, "--name") ?? Path.GetFileNameWithoutExtension(sourcePath);
        var cmdName = SanitizeCommandName(nameRaw);

        var bundle = AppInstaller.CompileSourceToBundle(sourcePath, cmdName);
        var (appPath, launcherPath) = AppInstaller.InstallAppBundle(cmdName, bundle);

        Console.WriteLine($"Приложение установлено: {appPath}");
        Console.WriteLine($"Лаунчер создан: {launcherPath}");
        Console.WriteLine($"Добавьте в PATH каталог: {AppInstaller.UserBinDir()}");
        return 0;
    }

    private static int CommandPaths(string[] args)
    {
        var isShort = HasFlag(args, "--short");
        if (isShort)
        {
            Console.WriteLine(AppInstaller.UserBinDir());
            return 0;
        }

        Console.WriteLine($"apps: {AppInstaller.UserAppsDir()}");
        Console.WriteLine($"bin:  {AppInstaller.UserBinDir()}");
        return 0;
    }

    private static int CommandDeps(string[] args)
    {
        var action = "install";
        if (args.Length > 1 && !args[1].StartsWith("-", StringComparison.Ordinal))
        {
            action = args[1];
        }

        if (action != "install" && action != "list")
        {
            throw new YasnException("deps action должен быть install или list");
        }

        var clean = HasFlag(args, "--clean");
        var all = HasFlag(args, "--all");

        if (action == "install")
        {
            var (manifest, installed) = DependencyManager.InstallDependencies(clean: clean);
            Console.WriteLine($"[yasn] Config: {manifest.ConfigPath}");
            if (manifest.Specs.Count == 0)
            {
                Console.WriteLine("[yasn] no dependencies in [dependencies].");
                return 0;
            }

            foreach (var item in installed)
            {
                var refSuffix = item.Spec.Ref is null ? string.Empty : $"#{item.Spec.Ref}";
                var scope = item.Direct ? "direct" : "transitive";
                var requestedBy = item.RequestedBy is null ? string.Empty : $" (from {item.RequestedBy})";
                Console.WriteLine($"[yasn] installed [{scope}] {item.Spec.Name}: {item.Spec.Kind} {item.Spec.Source}{refSuffix} -> {item.Target}{requestedBy}");
            }

            Console.WriteLine($"[yasn] Lock: {manifest.LockPath}");
            return 0;
        }

        var (listManifest, statuses) = DependencyManager.ListDependencies(includeTransitive: all);
        Console.WriteLine($"[yasn] Config: {listManifest.ConfigPath}");
        if (statuses.Count == 0)
        {
            Console.WriteLine("[yasn] no dependencies in [dependencies].");
            return 0;
        }

        foreach (var item in statuses)
        {
            var state = item.Installed ? "installed" : "missing";
            var refSuffix = item.Spec.Ref is null ? string.Empty : $"#{item.Spec.Ref}";
            var resolved = item.Resolved is null ? string.Empty : $" ({item.Resolved})";
            var scope = item.Direct ? "direct" : "transitive";
            var requestedBy = item.RequestedBy is null ? string.Empty : $" (from {item.RequestedBy})";
            Console.WriteLine($"[yasn] {state,-9} [{scope}] {item.Spec.Name}: {item.Spec.Kind} {item.Spec.Source}{refSuffix}{resolved}{requestedBy}");
        }

        return 0;
    }

    private static string RequireArg(string[] args, int index, string message)
    {
        if (index < args.Length)
        {
            return args[index];
        }

        throw new YasnException(message);
    }

    private static string? ParseOption(string[] args, string option)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == option)
            {
                if (i + 1 >= args.Length)
                {
                    throw new YasnException($"Отсутствует значение для опции {option}");
                }

                return args[i + 1];
            }
        }

        return null;
    }

    private static bool HasFlag(string[] args, string flag)
    {
        return args.Any(arg => arg == flag);
    }

    private static int? ParseNullableInt(string? raw, string error)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (int.TryParse(raw, out var value))
        {
            return value;
        }

        throw new YasnException($"{error}: {raw}");
    }

    private static string SanitizeCommandName(string value)
    {
        var name = value.Trim();
        if (name.Length == 0)
        {
            throw new YasnException("Имя команды не может быть пустым");
        }

        name = Regex.Replace(name, "\\s+", "_");
        name = Regex.Replace(name, "[^\\w\\-]", "_");
        if (name.Length == 0)
        {
            throw new YasnException("После нормализации имя команды оказалось пустым");
        }

        return name;
    }

    private static int HelpAndReturn()
    {
        PrintHelp();
        return 0;
    }

    private static int VersionAndReturn()
    {
        Console.WriteLine("yasn 1.0.0-native");
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.WriteLine($"Неизвестная команда: {command}");
        PrintHelp();
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("yasn (native compiler + VM)");
        Console.WriteLine("Команды:");
        Console.WriteLine("  run <file.яс|dev|start> [--backend ...] [--host ...] [--port ...]");
        Console.WriteLine("  dev [--backend ...] [--host ...] [--port ...]");
        Console.WriteLine("  start [--backend ...] [--host ...] [--port ...]");
        Console.WriteLine("  serve <backend.яс> [--host 127.0.0.1] [--port 8000]");
        Console.WriteLine("  check <file.яс>");
        Console.WriteLine("  test [path|file] [--pattern *_test.яс] [--fail-fast] [--verbose]");
        Console.WriteLine("  build <file.яс> [-o out.ybc]");
        Console.WriteLine("  exec <file.ybc>");
        Console.WriteLine("  pack <file.яс> [-o out.yapp] [--name app]");
        Console.WriteLine("  run-app <file.yapp>");
        Console.WriteLine("  install-app <file.яс> [--name app]");
        Console.WriteLine("  paths [--short]");
        Console.WriteLine("  deps [install|list] [--clean] [--all]");
    }
}
