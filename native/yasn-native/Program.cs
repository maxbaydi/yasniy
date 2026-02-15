using System.Text.RegularExpressions;
using YasnNative.App;
using YasnNative.Bytecode;
using YasnNative.Config;
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
            Console.WriteLine($"error: file not found: {ex.FileName}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"runtime error: {ex.Message}");
            return 1;
        }
    }

    private static int CommandRun(string[] args)
    {
        var target = RequireArg(args, 1, "run requires path to source file or dev/start mode");
        var backend = ParseOption(args, "--backend");
        var host = ParseOption(args, "--host");
        var portRaw = ParseOption(args, "--port");
        var port = ParseNullableInt(portRaw, "Invalid port");

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
        var port = ParseNullableInt(portRaw, "Invalid port");
        return ProjectRunner.RunMode(mode, backend, host, port);
    }

    private static int CommandServe(string[] args)
    {
        var sourcePath = RequireArg(args, 1, "serve requires path to backend source file");
        var host = ParseOption(args, "--host") ?? "127.0.0.1";
        var portRaw = ParseOption(args, "--port") ?? "8000";
        if (!int.TryParse(portRaw, out var port))
        {
            throw new YasnException($"Invalid port: {portRaw}");
        }

        BackendServer.Serve(sourcePath, host, port);
        return 0;
    }

    private static int CommandCheck(string[] args)
    {
        var sourcePath = RequireArg(args, 1, "check requires path to source file");
        var fullPath = Path.GetFullPath(sourcePath);
        var source = File.ReadAllText(fullPath);
        _ = Pipeline.CompileSource(source, fullPath);
        Console.WriteLine("Check passed: no errors found.");
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
        var sourcePath = RequireArg(args, 1, "build requires path to source file");
        var outputPath = ParseOption(args, "-o") ?? ParseOption(args, "--output");

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var source = File.ReadAllText(fullSourcePath);
        var program = Pipeline.CompileSource(source, fullSourcePath);
        var output = outputPath is null
            ? Path.ChangeExtension(fullSourcePath, ".ybc")
            : Path.GetFullPath(outputPath);

        File.WriteAllBytes(output, BytecodeCodec.EncodeProgram(program));
        Console.WriteLine($"Bytecode saved: {output}");
        return 0;
    }

    private static int CommandExec(string[] args)
    {
        var bytecodePath = RequireArg(args, 1, "exec requires path to .ybc file");
        var fullPath = Path.GetFullPath(bytecodePath);
        var program = BytecodeCodec.DecodeProgram(File.ReadAllBytes(fullPath), fullPath);
        var vm = new VirtualMachine(program, fullPath);
        vm.Run();
        return 0;
    }

    private static int CommandPack(string[] args)
    {
        var sourcePath = RequireArg(args, 1, "pack requires path to .яс source file");
        var outputPath = ParseOption(args, "-o") ?? ParseOption(args, "--output");
        var uiDistRaw = ParseOption(args, "--ui-dist");
        var metadata = AppProjectMetadata.LoadForSource(sourcePath);
        var fallbackName = Path.GetFileNameWithoutExtension(sourcePath);
        var explicitName = ParseOption(args, "--name");
        var appName = explicitName ?? metadata.ResolveTechnicalName(fallbackName);
        var displayName = explicitName ?? metadata.ResolveDisplayName(appName);
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var bundle = BuildBundleFromSource(
            sourcePath,
            new AppBundleMetadata(
                Name: appName,
                DisplayName: displayName,
                Description: metadata.Description,
                AppVersion: metadata.Version,
                Publisher: metadata.Publisher),
            uiDistRaw,
            metadata.ConfigPath);
        var output = outputPath is null
            ? Path.ChangeExtension(fullSourcePath, ".yapp")
            : Path.GetFullPath(outputPath);
        File.WriteAllBytes(output, bundle);
        Console.WriteLine($"App packed: {output}");
        if (!string.IsNullOrWhiteSpace(uiDistRaw))
        {
            Console.WriteLine("[yasn] UI assets embedded.");
        }
        return 0;
    }

    private static int CommandRunApp(string[] args)
    {
        var appPath = RequireArg(args, 1, "run-app requires path to .yapp file");
        var host = ParseOption(args, "--host") ?? "127.0.0.1";
        var portRaw = ParseOption(args, "--port") ?? "8080";
        if (!int.TryParse(portRaw, out var port))
        {
            throw new YasnException($"Invalid port: {portRaw}");
        }
        var fullPath = Path.GetFullPath(appPath);
        var (bundle, program) = AppBundleCodec.DecodeBundleToProgram(File.ReadAllBytes(fullPath), fullPath);
        if (bundle.UiDistZip is { Length: > 0 })
        {
            var backend = BackendKernel.FromProgram(program, fullPath, bundle.Schema);
            var assets = UiAssetManifest.FromZip(bundle.UiDistZip);
            AppRuntimeServer.Serve(bundle, backend, assets, host, port);
            return 0;
        }
        var vm = new VirtualMachine(program, fullPath);
        vm.Run();
        var appLabel = string.IsNullOrWhiteSpace(bundle.DisplayName) ? bundle.Name : bundle.DisplayName;
        Console.WriteLine($"[yasn] App '{appLabel}' finished.");
        return 0;
    }

    private static int CommandInstallApp(string[] args)
    {
        var sourcePath = RequireArg(args, 1, "install-app requires path to .яс source file");
        var uiDistRaw = ParseOption(args, "--ui-dist");
        var metadata = AppProjectMetadata.LoadForSource(sourcePath);
        var fallbackName = Path.GetFileNameWithoutExtension(sourcePath);
        var explicitName = ParseOption(args, "--name");
        var nameRaw = explicitName ?? metadata.ResolveTechnicalName(fallbackName);
        var displayName = explicitName ?? metadata.ResolveDisplayName(nameRaw);
        var cmdName = SanitizeCommandName(nameRaw);
        var bundle = BuildBundleFromSource(
            sourcePath,
            new AppBundleMetadata(
                Name: nameRaw,
                DisplayName: displayName,
                Description: metadata.Description,
                AppVersion: metadata.Version,
                Publisher: metadata.Publisher),
            uiDistRaw,
            metadata.ConfigPath);
        var (appPath, launcherPath) = AppInstaller.InstallAppBundle(cmdName, bundle);
        Console.WriteLine($"App installed: {appPath}");
        Console.WriteLine($"Launcher created: {launcherPath}");
        if (AppInstaller.IsUserBinInPath())
        {
            Console.WriteLine($"Command available: {cmdName}");
        }
        else
        {
            Console.WriteLine($"Add to PATH: {AppInstaller.UserBinDir()}");
            Console.WriteLine("Open a new terminal after updating PATH.");
        }
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
            throw new YasnException("deps action must be install or list");
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
                    throw new YasnException($"Missing value for option {option}");
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
            throw new YasnException("Command name cannot be empty");
        }

        name = Regex.Replace(name, "\\s+", "_");
        name = Regex.Replace(name, "[^\\w\\-]", "_");
        if (name.Length == 0)
        {
            throw new YasnException("Command name is empty after normalization");
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
        Console.WriteLine($"Unknown command: {command}");
        PrintHelp();
        return 2;
    }

    private static byte[] BuildBundleFromSource(
        string sourcePath,
        AppBundleMetadata metadata,
        string? uiDistRaw,
        string? configPath)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var source = File.ReadAllText(fullSourcePath);
        var loadedProgram = Pipeline.LoadProgram(source, fullSourcePath);
        var program = Pipeline.CompileProgram(loadedProgram, fullSourcePath);
        var bytecode = BytecodeCodec.EncodeProgram(program);
        var schema = FunctionSchemaBuilder.FromProgramNode(loadedProgram);
        byte[]? uiDistZip = null;
        if (!string.IsNullOrWhiteSpace(uiDistRaw))
        {
            var uiDistPath = ResolveUiDistPath(uiDistRaw, sourcePath, configPath);
            uiDistZip = UiAssetManifest.PackDirectory(uiDistPath);
        }
        return AppBundleCodec.CreateBundle(
            metadata with
            {
                Schema = schema,
            },
            bytecode,
            uiDistZip);
    }

    private static string ResolveUiDistPath(string uiDistRaw, string sourcePath, string? configPath)
    {
        if (Path.IsPathRooted(uiDistRaw))
        {
            return Path.GetFullPath(uiDistRaw);
        }
        var projectRoot = !string.IsNullOrWhiteSpace(configPath)
            ? Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(projectRoot, uiDistRaw));
    }

    private static void PrintHelp()
    {
        Console.WriteLine("yasn (native compiler + VM)");
        Console.WriteLine("Commands:");
        Console.WriteLine("  run <file|dev|start> [--backend ...] [--host ...] [--port ...]");
        Console.WriteLine("  dev [--backend ...] [--host ...] [--port ...]");
        Console.WriteLine("  start [--backend ...] [--host ...] [--port ...]");
        Console.WriteLine("  serve <backend-file> [--host 127.0.0.1] [--port 8000]");
        Console.WriteLine("  check <file>");
        Console.WriteLine("  test [path|file] [--pattern *_test.*] [--fail-fast] [--verbose]");
        Console.WriteLine("  build <file> [-o out.ybc]");
        Console.WriteLine("  exec <file.ybc>");
        Console.WriteLine("  pack <file.яс> [-o out.yapp] [--name app] [--ui-dist ui/dist]");
        Console.WriteLine("  run-app <file.yapp> [--host 127.0.0.1] [--port 8080]");
        Console.WriteLine("  install-app <file.яс> [--name app] [--ui-dist ui/dist]");
        Console.WriteLine("  paths [--short]");
        Console.WriteLine("  deps [install|list] [--clean] [--all]");
    }
}

