using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Tomlyn.Model;
using YasnNative.Config;
using YasnNative.Core;

namespace YasnNative.Deps;

public sealed record DependencySpec(string Name, string Kind, string Source, string? Ref = null);

public sealed record DependenciesManifest(
    string ProjectRoot,
    string ConfigPath,
    string DepsRoot,
    string LockPath,
    List<DependencySpec> Specs);

public sealed record DependencyInstallResult(
    DependencySpec Spec,
    string Target,
    string Resolved,
    bool Direct,
    string? RequestedBy = null);

public sealed record DependencyStatus(
    DependencySpec Spec,
    string Target,
    bool Installed,
    string? Resolved = null,
    bool Direct = true,
    string? RequestedBy = null);

internal sealed record QueuedDependency(DependencySpec Spec, string ProjectRoot, bool Direct, string? RequestedBy = null);

internal sealed record LockDependency(DependencySpec Spec, string Target, string? Resolved, bool Direct, string? RequestedBy);

public static class DependencyManager
{
    private const string DepsDirRelative = ".yasn/deps";
    private const string LockFileRelative = ".yasn/deps.lock.json";

    public static DependenciesManifest LoadManifest(string? cwd = null)
    {
        var start = cwd ?? Directory.GetCurrentDirectory();
        var projectRoot = TomlUtil.FindProjectRoot(start);
        var configPath = TomlUtil.FindConfig(start)
            ?? throw new YasnException("Не найден yasn.toml. Зависимости требуют конфиг проекта.");

        var data = TomlUtil.ReadToml(configPath);
        var specs = ParseDependencies(data, configPath);
        var depsRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectRoot, DepsDirRelative));
        var lockPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectRoot, LockFileRelative));

        return new DependenciesManifest(projectRoot, configPath, depsRoot, lockPath, specs);
    }

    public static (DependenciesManifest Manifest, List<DependencyInstallResult> Installed) InstallDependencies(string? cwd = null, bool clean = false)
    {
        var manifest = LoadManifest(cwd);
        Directory.CreateDirectory(manifest.DepsRoot);

        var queue = new Queue<QueuedDependency>(manifest.Specs.Select(spec => new QueuedDependency(spec, manifest.ProjectRoot, true, null)));
        var planned = new Dictionary<string, (string Identity, QueuedDependency Dep)>(StringComparer.Ordinal);
        var results = new List<DependencyInstallResult>();

        while (queue.Count > 0)
        {
            var dep = queue.Dequeue();
            var identity = DependencyIdentity(dep.Spec, dep.ProjectRoot);
            if (planned.TryGetValue(dep.Spec.Name, out var existing))
            {
                if (!string.Equals(existing.Identity, identity, StringComparison.Ordinal))
                {
                    var prevSource = DependencySourceForError(existing.Dep.Spec, existing.Dep.ProjectRoot);
                    var newSource = DependencySourceForError(dep.Spec, dep.ProjectRoot);
                    throw new YasnException(
                        $"Конфликт зависимостей '{dep.Spec.Name}': {prevSource} и {newSource}. Используйте единый источник и версию.",
                        path: manifest.ConfigPath);
                }

                continue;
            }

            planned[dep.Spec.Name] = (identity, dep);
            var target = System.IO.Path.Combine(manifest.DepsRoot, dep.Spec.Name);

            string resolved;
            string nestedRoot;
            if (dep.Spec.Kind == "path")
            {
                (resolved, nestedRoot) = InstallFromPath(dep.Spec, dep.ProjectRoot, target);
            }
            else if (dep.Spec.Kind == "git")
            {
                (resolved, nestedRoot) = InstallFromGit(dep.Spec, dep.ProjectRoot, target);
            }
            else
            {
                throw new YasnException($"Неподдерживаемый тип зависимости: {dep.Spec.Kind}", path: manifest.ConfigPath);
            }

            results.Add(new DependencyInstallResult(dep.Spec, target, resolved, dep.Direct, dep.RequestedBy));

            foreach (var nested in LoadNestedDependencySpecs(nestedRoot))
            {
                queue.Enqueue(new QueuedDependency(nested, nestedRoot, false, dep.Spec.Name));
            }
        }

        if (clean)
        {
            var wanted = results.Select(item => item.Spec.Name).ToHashSet(StringComparer.Ordinal);
            if (Directory.Exists(manifest.DepsRoot))
            {
                foreach (var dir in Directory.EnumerateDirectories(manifest.DepsRoot))
                {
                    var name = System.IO.Path.GetFileName(dir);
                    if (wanted.Contains(name))
                    {
                        continue;
                    }

                    RemoveTree(dir);
                }
            }
        }

        WriteLock(manifest, results);
        return (manifest, results);
    }

    public static (DependenciesManifest Manifest, List<DependencyStatus> Statuses) ListDependencies(string? cwd = null, bool includeTransitive = false)
    {
        var manifest = LoadManifest(cwd);
        var statuses = new List<DependencyStatus>();

        foreach (var spec in manifest.Specs)
        {
            var target = System.IO.Path.Combine(manifest.DepsRoot, spec.Name);
            var installed = Directory.Exists(target);
            string? resolved = null;
            if (installed)
            {
                resolved = spec.Kind == "git"
                    ? GitHead(target)
                    : ResolveDepPath(spec.Source, manifest.ProjectRoot);
            }

            statuses.Add(new DependencyStatus(spec, target, installed, resolved, true, null));
        }

        if (includeTransitive)
        {
            var seen = statuses.Select(item => item.Spec.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var locked in ReadLockDependencies(manifest))
            {
                if (seen.Contains(locked.Spec.Name))
                {
                    continue;
                }

                var installed = Directory.Exists(locked.Target);
                var resolved = locked.Resolved;
                if (installed && resolved is null && locked.Spec.Kind == "git")
                {
                    resolved = GitHead(locked.Target);
                }

                statuses.Add(new DependencyStatus(locked.Spec, locked.Target, installed, resolved, locked.Direct, locked.RequestedBy));
                seen.Add(locked.Spec.Name);
            }

            statuses = statuses.OrderBy(item => item.Direct ? 0 : 1).ThenBy(item => item.Spec.Name, StringComparer.Ordinal).ToList();
        }

        return (manifest, statuses);
    }

    private static List<DependencySpec> ParseDependencies(TomlTable data, string configPath)
    {
        if (!data.TryGetValue("dependencies", out var raw) || raw is null)
        {
            return [];
        }

        if (raw is not TomlTable deps)
        {
            throw new YasnException("Секция [dependencies] должна быть объектом", path: configPath);
        }

        var specs = new List<DependencySpec>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (nameRaw, value) in deps)
        {
            var depName = nameRaw?.Trim();
            if (string.IsNullOrWhiteSpace(depName))
            {
                throw new YasnException("Имя зависимости должно быть непустой строкой", path: configPath);
            }

            if (!seen.Add(depName))
            {
                throw new YasnException($"Повтор имени зависимости: {depName}", path: configPath);
            }

            string source;
            string? refName = null;

            switch (value)
            {
                case string sourceStr:
                    source = sourceStr.Trim();
                    break;
                case TomlTable depObj:
                {
                    if (!depObj.TryGetValue("source", out var sourceRaw) || sourceRaw is not string sourceValue || string.IsNullOrWhiteSpace(sourceValue))
                    {
                        throw new YasnException($"dependencies.{depName}.source должен быть непустой строкой", path: configPath);
                    }

                    source = sourceValue.Trim();
                    if (depObj.TryGetValue("ref", out var refRaw) && refRaw is not null)
                    {
                        if (refRaw is not string refStr || string.IsNullOrWhiteSpace(refStr))
                        {
                            throw new YasnException($"dependencies.{depName}.ref должен быть непустой строкой", path: configPath);
                        }

                        refName = refStr.Trim();
                    }

                    break;
                }
                default:
                    throw new YasnException($"dependencies.{depName} должен быть строкой или объектом {{source, ref?}}", path: configPath);
            }

            var (kind, normalizedSource, normalizedRef) = NormalizeSource(source, refName, configPath);
            specs.Add(new DependencySpec(depName, kind, normalizedSource, normalizedRef));
        }

        return specs;
    }

    private static (string Kind, string Source, string? Ref) NormalizeSource(string source, string? refName, string configPath)
    {
        if (source.StartsWith("git+", StringComparison.Ordinal))
        {
            var raw = source[4..].Trim();
            if (raw.Length == 0)
            {
                throw new YasnException("Пустой git-источник зависимости", path: configPath);
            }

            if (raw.Contains('#', StringComparison.Ordinal))
            {
                var split = raw.Split('#', 2);
                raw = split[0].Trim();
                var hashRef = split[1].Trim();
                if (refName is null && hashRef.Length > 0)
                {
                    refName = hashRef;
                }
            }

            if (raw.Length == 0)
            {
                throw new YasnException("Пустой URL git-источника зависимости", path: configPath);
            }

            return ("git", raw, refName);
        }

        if (source.StartsWith("path:", StringComparison.Ordinal))
        {
            var raw = source[5..].Trim();
            if (raw.Length == 0)
            {
                throw new YasnException("Пустой path-источник зависимости", path: configPath);
            }

            return ("path", raw, null);
        }

        return ("path", source, null);
    }
    private static (string Resolved, string NestedRoot) InstallFromPath(DependencySpec spec, string projectRoot, string target)
    {
        var sourcePath = ResolveDepPath(spec.Source, projectRoot);
        if (!Directory.Exists(sourcePath))
        {
            throw new YasnException($"Путь зависимости не найден: {sourcePath}");
        }

        if (Directory.Exists(target))
        {
            RemoveTree(target);
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        CopyDirectory(sourcePath, target);
        return (sourcePath, sourcePath);
    }

    private static (string Resolved, string NestedRoot) InstallFromGit(DependencySpec spec, string projectRoot, string target)
    {
        var source = spec.Source;
        if (IsLocalGitSource(source))
        {
            source = ResolveDepPath(source, projectRoot);
            if (!Directory.Exists(source) && !File.Exists(source))
            {
                throw new YasnException($"Git-источник не найден: {source}");
            }
        }

        if (Directory.Exists(target))
        {
            RemoveTree(target);
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);

        RunCommand(["git", "clone", "--depth", "1", source, target], System.IO.Path.GetDirectoryName(target)!);
        if (!string.IsNullOrWhiteSpace(spec.Ref))
        {
            RunCommand(["git", "fetch", "--depth", "1", "origin", spec.Ref!], target);
            RunCommand(["git", "checkout", "FETCH_HEAD"], target);
        }

        return (GitHead(target), target);
    }

    private static IEnumerable<DependencySpec> LoadNestedDependencySpecs(string nestedRoot)
    {
        var configPath = System.IO.Path.Combine(nestedRoot, "yasn.toml");
        if (!File.Exists(configPath))
        {
            return [];
        }

        var data = TomlUtil.ReadToml(configPath);
        return ParseDependencies(data, configPath);
    }

    private static bool IsLocalGitSource(string source)
    {
        var src = source.Trim();
        if (src.Contains("://", StringComparison.Ordinal) || src.StartsWith("git@", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static string ResolveDepPath(string raw, string projectRoot)
    {
        var expanded = Environment.ExpandEnvironmentVariables(raw);
        if (expanded.StartsWith("~", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            expanded = System.IO.Path.Combine(home, expanded[1..].TrimStart('/', '\\'));
        }

        if (System.IO.Path.IsPathRooted(expanded))
        {
            return System.IO.Path.GetFullPath(expanded);
        }

        return System.IO.Path.GetFullPath(System.IO.Path.Combine(projectRoot, expanded));
    }

    private static string DependencyIdentity(DependencySpec spec, string projectRoot)
    {
        return spec.Kind switch
        {
            "path" => $"path::{ResolveDepPath(spec.Source, projectRoot)}",
            "git" => $"git::{spec.Source}#{spec.Ref ?? string.Empty}",
            _ => $"{spec.Kind}::{spec.Source}#{spec.Ref ?? string.Empty}",
        };
    }

    private static string DependencySourceForError(DependencySpec spec, string projectRoot)
    {
        return spec.Kind switch
        {
            "path" => $"path:{ResolveDepPath(spec.Source, projectRoot)}",
            "git" => $"git+{spec.Source}{(spec.Ref is null ? string.Empty : "#" + spec.Ref)}",
            _ => $"{spec.Kind}:{spec.Source}",
        };
    }

    private static string GitHead(string repoDir)
    {
        var output = RunCommand(["git", "rev-parse", "HEAD"], repoDir);
        return output.Trim();
    }

    private static string RunCommand(List<string> cmd, string cwd)
    {
        var psi = new ProcessStartInfo
        {
            FileName = cmd[0],
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in cmd.Skip(1))
        {
            psi.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new YasnException($"Не удалось запустить процесс: {cmd[0]}");
        }
        catch (Exception ex)
        {
            throw new YasnException($"Не найдено внешнее приложение: {cmd[0]} ({ex.Message})");
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new YasnException($"Команда завершилась с ошибкой ({process.ExitCode}): {string.Join(" ", cmd)}\n{details.Trim()}");
        }

        return stdout;
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var name = System.IO.Path.GetFileName(file);
            if (ShouldSkipName(name))
            {
                continue;
            }

            var target = System.IO.Path.Combine(targetDir, name);
            File.Copy(file, target, overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            var name = System.IO.Path.GetFileName(dir);
            if (ShouldSkipName(name))
            {
                continue;
            }

            CopyDirectory(dir, System.IO.Path.Combine(targetDir, name));
        }
    }

    private static bool ShouldSkipName(string name)
    {
        return name is ".git" or "__pycache__" or ".venv" or "node_modules";
    }

    private static void RemoveTree(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var dir = new DirectoryInfo(path);
        foreach (var item in dir.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            item.Attributes = FileAttributes.Normal;
        }

        dir.Attributes = FileAttributes.Normal;
        Directory.Delete(path, recursive: true);
    }

    private static void WriteLock(DependenciesManifest manifest, List<DependencyInstallResult> installs)
    {
        var dependencies = installs
            .OrderBy(item => item.Spec.Name, StringComparer.Ordinal)
            .Select(item => new Dictionary<string, object?>
            {
                ["name"] = item.Spec.Name,
                ["kind"] = item.Spec.Kind,
                ["source"] = item.Spec.Source,
                ["ref"] = item.Spec.Ref,
                ["resolved"] = item.Resolved,
                ["target"] = SerializeTarget(item.Target, manifest.ProjectRoot),
                ["direct"] = item.Direct,
                ["requested_by"] = item.RequestedBy,
            })
            .ToList();

        var payload = new Dictionary<string, object?>
        {
            ["version"] = 1,
            ["config"] = SerializeTarget(manifest.ConfigPath, manifest.ProjectRoot),
            ["dependencies"] = dependencies,
        };

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(manifest.LockPath)!);
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        File.WriteAllText(manifest.LockPath, json);
    }

    private static string SerializeTarget(string path, string projectRoot)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var fullProject = System.IO.Path.GetFullPath(projectRoot);

        if (fullPath.StartsWith(fullProject, StringComparison.OrdinalIgnoreCase))
        {
            return System.IO.Path.GetRelativePath(fullProject, fullPath);
        }

        return fullPath;
    }

    private static List<LockDependency> ReadLockDependencies(DependenciesManifest manifest)
    {
        if (!File.Exists(manifest.LockPath))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifest.LockPath));
            var root = doc.RootElement;
            if (!root.TryGetProperty("dependencies", out var depsElement) || depsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<LockDependency>();
            foreach (var item in depsElement.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
                var kind = item.TryGetProperty("kind", out var kindEl) ? kindEl.GetString() ?? string.Empty : string.Empty;
                var source = item.TryGetProperty("source", out var sourceEl) ? sourceEl.GetString() ?? string.Empty : string.Empty;
                var refValue = item.TryGetProperty("ref", out var refEl) && refEl.ValueKind != JsonValueKind.Null
                    ? refEl.GetString()
                    : null;
                var resolved = item.TryGetProperty("resolved", out var resolvedEl) && resolvedEl.ValueKind != JsonValueKind.Null
                    ? resolvedEl.GetString()
                    : null;
                var targetRaw = item.TryGetProperty("target", out var targetEl)
                    ? targetEl.GetString() ?? string.Empty
                    : string.Empty;
                var direct = item.TryGetProperty("direct", out var directEl) && directEl.ValueKind == JsonValueKind.True;
                var requestedBy = item.TryGetProperty("requested_by", out var reqEl) && reqEl.ValueKind != JsonValueKind.Null
                    ? reqEl.GetString()
                    : null;

                if (name.Length == 0 || kind.Length == 0 || source.Length == 0 || targetRaw.Length == 0)
                {
                    continue;
                }

                var spec = new DependencySpec(name, kind, source, refValue);
                var target = System.IO.Path.IsPathRooted(targetRaw)
                    ? targetRaw
                    : System.IO.Path.GetFullPath(System.IO.Path.Combine(manifest.ProjectRoot, targetRaw));
                result.Add(new LockDependency(spec, target, resolved, direct, requestedBy));
            }

            return result;
        }
        catch
        {
            return [];
        }
    }
}

