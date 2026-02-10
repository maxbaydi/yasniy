using System.Diagnostics;
using Tomlyn.Model;
using YasnNative.Config;
using YasnNative.Core;

namespace YasnNative.Runner;

public sealed record RunProfile(
    string Mode,
    string Backend,
    string Host,
    int Port,
    string? FrontendCommand,
    string? FrontendCwd,
    string ProjectRoot,
    string? ConfigPath);

public static class ProjectRunner
{
    public static int RunMode(string mode, string? backend = null, string? host = null, int? port = null, string? cwd = null)
    {
        var profile = LoadRunProfile(mode, backend, host, port, cwd);

        var backendPsi = BuildSelfProcessStartInfo([
            "serve",
            profile.Backend,
            "--host",
            profile.Host,
            "--port",
            profile.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ]);
        backendPsi.WorkingDirectory = profile.ProjectRoot;

        Console.WriteLine($"[yasn] Mode: {profile.Mode}");
        if (!string.IsNullOrWhiteSpace(profile.ConfigPath))
        {
            Console.WriteLine($"[yasn] Config: {profile.ConfigPath}");
        }
        Console.WriteLine($"[yasn] Backend: serve {profile.Backend} --host {profile.Host} --port {profile.Port}");

        using var backendProcess = Process.Start(backendPsi)
            ?? throw new YasnException("Не удалось запустить backend процесс");

        Process? frontendProcess = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(profile.FrontendCommand))
            {
                var frontendCwd = ResolveFrontendCwd(profile);
                Console.WriteLine($"[yasn] Frontend: {profile.FrontendCommand} (cwd={frontendCwd})");
                frontendProcess = StartShellCommand(profile.FrontendCommand!, frontendCwd);
            }

            if (frontendProcess is null)
            {
                backendProcess.WaitForExit();
                return backendProcess.ExitCode;
            }

            while (true)
            {
                if (backendProcess.HasExited)
                {
                    Console.WriteLine($"[yasn] Backend exited with code {backendProcess.ExitCode}");
                    StopProcess(frontendProcess);
                    return backendProcess.ExitCode;
                }

                if (frontendProcess.HasExited)
                {
                    Console.WriteLine($"[yasn] Frontend exited with code {frontendProcess.ExitCode}");
                    StopProcess(backendProcess);
                    return frontendProcess.ExitCode;
                }

                Thread.Sleep(200);
            }
        }
        catch (OperationCanceledException)
        {
            StopProcess(frontendProcess);
            StopProcess(backendProcess);
            return 130;
        }
        finally
        {
            frontendProcess?.Dispose();
        }
    }

    public static RunProfile LoadRunProfile(
        string mode,
        string? backend = null,
        string? host = null,
        int? port = null,
        string? cwd = null)
    {
        var current = System.IO.Path.GetFullPath(cwd ?? Directory.GetCurrentDirectory());
        var configPath = TomlUtil.FindConfig(current);
        var projectRoot = configPath is null
            ? current
            : System.IO.Path.GetDirectoryName(configPath) ?? current;

        var data = configPath is null ? new TomlTable() : TomlUtil.ReadToml(configPath);
        var runCfg = TomlUtil.GetTable(data, "run", configPath);
        var modeCfg = GetSubTable(runCfg, mode, configPath);

        var backendPath = (backend
                ?? TomlUtil.GetString(modeCfg, "backend", configPath)
                ?? TomlUtil.GetString(runCfg, "backend", configPath)
                ?? DetectDefaultBackend(projectRoot)
                ?? string.Empty)
            .Trim();

        if (backendPath.Length == 0)
        {
            throw new YasnException("Не найден backend entrypoint. Укажите [run].backend в yasn.toml или передайте --backend");
        }

        var hostValue = (host
                ?? TomlUtil.GetString(modeCfg, "host", configPath)
                ?? TomlUtil.GetString(runCfg, "host", configPath)
                ?? "127.0.0.1")
            .Trim();

        var portValue = port
            ?? TomlUtil.GetInt(modeCfg, "port", configPath)
            ?? TomlUtil.GetInt(runCfg, "port", configPath)
            ?? 8000;

        var frontendCommand =
            TomlUtil.GetString(modeCfg, "frontend", configPath)
            ?? TomlUtil.GetString(modeCfg, "frontend_cmd", configPath)
            ?? TomlUtil.GetString(runCfg, "frontend", configPath)
            ?? TomlUtil.GetString(runCfg, "frontend_cmd", configPath);

        var frontendCwd =
            TomlUtil.GetString(modeCfg, "frontend_cwd", configPath)
            ?? TomlUtil.GetString(modeCfg, "frontend_dir", configPath)
            ?? TomlUtil.GetString(runCfg, "frontend_cwd", configPath)
            ?? TomlUtil.GetString(runCfg, "frontend_dir", configPath);

        return new RunProfile(mode, backendPath, hostValue, portValue, frontendCommand, frontendCwd, projectRoot, configPath);
    }

    private static TomlTable GetSubTable(TomlTable table, string key, string? pathForError)
    {
        if (!table.TryGetValue(key, out var raw) || raw is null)
        {
            return new TomlTable();
        }

        if (raw is TomlTable t)
        {
            return t;
        }

        throw new YasnException($"Секция [run.{key}] должна быть объектом", path: pathForError);
    }

    private static string? DetectDefaultBackend(string root)
    {
        var candidates = new[]
        {
            System.IO.Path.Combine(root, "backend", "main.яс"),
            System.IO.Path.Combine(root, "main.яс"),
            System.IO.Path.Combine(root, "app", "main.яс"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return System.IO.Path.GetRelativePath(root, candidate);
            }
        }

        return null;
    }

    private static string ResolveFrontendCwd(RunProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.FrontendCwd))
        {
            return profile.ProjectRoot;
        }

        return System.IO.Path.IsPathRooted(profile.FrontendCwd)
            ? System.IO.Path.GetFullPath(profile.FrontendCwd)
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(profile.ProjectRoot, profile.FrontendCwd));
    }

    private static Process StartShellCommand(string command, string cwd)
    {
        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            psi = new ProcessStartInfo("cmd.exe")
            {
                WorkingDirectory = cwd,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = false,
            };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(command);
        }
        else
        {
            psi = new ProcessStartInfo("/bin/sh")
            {
                WorkingDirectory = cwd,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = false,
            };
            psi.ArgumentList.Add("-lc");
            psi.ArgumentList.Add(command);
        }

        return Process.Start(psi) ?? throw new YasnException("Не удалось запустить frontend команду");
    }

    private static ProcessStartInfo BuildSelfProcessStartInfo(List<string> args)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new YasnException("Не удалось определить путь текущего исполняемого файла");
        }

        var psi = new ProcessStartInfo
        {
            FileName = processPath,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        var exeName = System.IO.Path.GetFileNameWithoutExtension(processPath);
        if (string.Equals(exeName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var dllPath = System.IO.Path.Combine(AppContext.BaseDirectory, "yasn.dll");
            psi.ArgumentList.Add(dllPath);
        }

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        return psi;
    }

    private static void StopProcess(Process? process)
    {
        if (process is null)
        {
            return;
        }

        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch
        {
            // ignore stop errors
        }
    }
}
