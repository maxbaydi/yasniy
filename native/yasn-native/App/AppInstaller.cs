using System.Text;
using YasnNative.Bytecode;
using YasnNative.Core;

namespace YasnNative.App;

public static class AppInstaller
{
    public static string UserHomeDir()
    {
        var appData = Environment.GetEnvironmentVariable("APPDATA");
        if (!string.IsNullOrWhiteSpace(appData))
        {
            return System.IO.Path.Combine(appData, "yasn");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return System.IO.Path.Combine(home, ".yasn");
    }

    public static string UserAppsDir()
    {
        return System.IO.Path.Combine(UserHomeDir(), "apps");
    }

    public static string UserBinDir()
    {
        return System.IO.Path.Combine(UserHomeDir(), "bin");
    }

    public static (string AppPath, string LauncherPath) InstallAppBundle(string name, byte[] bundleBytes)
    {
        var appsDir = UserAppsDir();
        var binDir = UserBinDir();
        Directory.CreateDirectory(appsDir);
        Directory.CreateDirectory(binDir);

        var appPath = System.IO.Path.Combine(appsDir, name + ".yapp");
        File.WriteAllBytes(appPath, bundleBytes);

        if (OperatingSystem.IsWindows())
        {
            var launcherPath = System.IO.Path.Combine(binDir, name + ".cmd");
            File.WriteAllText(launcherPath, MakeWindowsLauncher(name), Encoding.UTF8);
            return (appPath, launcherPath);
        }

        var shPath = System.IO.Path.Combine(binDir, name);
        File.WriteAllText(shPath, MakeUnixLauncher(name), Encoding.UTF8);
        TryMakeExecutable(shPath);
        return (appPath, shPath);
    }

    public static byte[] CompileSourceToBundle(string sourcePath, string appName)
    {
        var fullPath = System.IO.Path.GetFullPath(sourcePath);
        var source = File.ReadAllText(fullPath);
        var program = Pipeline.CompileSource(source, fullPath);
        var bytecode = BytecodeCodec.EncodeProgram(program);
        return AppBundleCodec.CreateBundle(appName, bytecode);
    }

    private static string MakeWindowsLauncher(string name)
    {
        return string.Join("\r\n", new[]
        {
            "@echo off",
            "setlocal",
            "set APP=%~dp0..\\apps\\" + name + ".yapp",
            "yasn run-app \"%APP%\" %*",
            "if %ERRORLEVEL% EQU 9009 dotnet \"%~dp0..\\..\\native\\yasn-native\\bin\\Release\\net10.0\\win-x64\\publish\\yasn.dll\" run-app \"%APP%\" %*",
            string.Empty,
        });
    }

    private static string MakeUnixLauncher(string name)
    {
        return string.Join('\n', new[]
        {
            "#!/usr/bin/env sh",
            "APP=\"$(dirname \"$0\")/../apps/" + name + ".yapp\"",
            "yasn run-app \"$APP\" \"$@\"",
            string.Empty,
        });
    }

    private static void TryMakeExecutable(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                           UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                           UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
                File.SetUnixFileMode(path, mode);
            }
        }
        catch
        {
            // best effort for unix permissions
        }
    }
}
