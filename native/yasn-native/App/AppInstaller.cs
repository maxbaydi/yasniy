using System.Text;
using YasnNative.Bytecode;
using YasnNative.Core;

namespace YasnNative.App;

public static class AppInstaller
{
    private static readonly Encoding LauncherEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static string UserHomeDir()
    {
        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                return System.IO.Path.Combine(localAppData, "yasn");
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
            {
                return System.IO.Path.Combine(appData, "yasn");
            }

            var localAppDataEnv = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrWhiteSpace(localAppDataEnv))
            {
                return System.IO.Path.Combine(localAppDataEnv, "yasn");
            }

            var appDataEnv = Environment.GetEnvironmentVariable("APPDATA");
            if (!string.IsNullOrWhiteSpace(appDataEnv))
            {
                return System.IO.Path.Combine(appDataEnv, "yasn");
            }
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

    public static bool IsUserBinInPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var target = NormalizePath(UserBinDir());

        foreach (var item in path.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(NormalizePath(item), target, comparison))
            {
                return true;
            }
        }

        return false;
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
            File.WriteAllText(launcherPath, MakeWindowsLauncher(), LauncherEncoding);

            var bashLauncherPath = System.IO.Path.Combine(binDir, name);
            File.WriteAllText(bashLauncherPath, MakeWindowsBashLauncher(), LauncherEncoding);

            return (appPath, launcherPath);
        }

        var shPath = System.IO.Path.Combine(binDir, name);
        File.WriteAllText(shPath, MakeUnixLauncher(name), LauncherEncoding);
        TryMakeExecutable(shPath);
        return (appPath, shPath);
    }

    public static byte[] CompileSourceToBundle(
        string sourcePath,
        AppBundleMetadata metadata,
        string? uiDistPath = null,
        string? projectRoot = null)
    {
        var fullPath = System.IO.Path.GetFullPath(sourcePath);
        var source = File.ReadAllText(fullPath);
        var loadedProgram = Pipeline.LoadProgram(source, fullPath);
        var program = Pipeline.CompileProgram(loadedProgram, fullPath);
        var bytecode = BytecodeCodec.EncodeProgram(program);
        var schema = FunctionSchemaBuilder.FromProgramNode(loadedProgram);

        byte[]? uiZip = null;
        if (!string.IsNullOrWhiteSpace(uiDistPath))
        {
            var uiPath = uiDistPath!;
            if (!System.IO.Path.IsPathRooted(uiPath))
            {
                var root = projectRoot
                    ?? System.IO.Path.GetDirectoryName(fullPath)
                    ?? Directory.GetCurrentDirectory();
                uiPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, uiPath));
            }

            uiZip = UiAssetManifest.PackDirectory(uiPath);
        }

        return AppBundleCodec.CreateBundle(metadata with { Schema = schema }, bytecode, uiZip);
    }

    private static string NormalizePath(string value)
    {
        return value.Trim().TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    }

    private static string MakeWindowsLauncher()
    {
        return string.Join("\r\n", new[]
        {
            "@echo off",
            "setlocal",
            "set \"APP=%~dp0..\\apps\\%~n0.yapp\"",
            "set \"YASN=%~dp0yasn.exe\"",
            "if exist \"%YASN%\" (",
            "  \"%YASN%\" run-app \"%APP%\" %*",
            ") else (",
            "  yasn run-app \"%APP%\" %*",
            ")",
            "exit /b %ERRORLEVEL%",
            string.Empty,
        });
    }

    private static string MakeWindowsBashLauncher()
    {
        return string.Join('\n', new[]
        {
            "#!/usr/bin/env sh",
            "SCRIPT_PATH=\"$(command -v -- \"$0\")\"",
            "SCRIPT_DIR=\"$(CDPATH= cd -- \"$(dirname -- \"$SCRIPT_PATH\")\" && pwd)\"",
            "APP_NAME=\"$(basename -- \"$SCRIPT_PATH\")\"",
            "APP=\"$SCRIPT_DIR/../apps/$APP_NAME.yapp\"",
            "if [ -x \"$SCRIPT_DIR/yasn.exe\" ]; then",
            "  \"$SCRIPT_DIR/yasn.exe\" run-app \"$APP\" \"$@\"",
            "else",
            "  yasn run-app \"$APP\" \"$@\"",
            "fi",
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
