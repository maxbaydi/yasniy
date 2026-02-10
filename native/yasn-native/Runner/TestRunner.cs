using System.Diagnostics;
using YasnNative.Core;
using YasnNative.Runtime;

namespace YasnNative.Runner;

public static class TestRunner
{
    private static readonly string[] DefaultPatterns = ["*_test.яс", "*.test.яс"];

    public static int Run(string? targetPath = null, string? pattern = null, bool failFast = false, bool verbose = false)
    {
        var startedAt = Stopwatch.StartNew();
        var resolvedTarget = ResolveTarget(targetPath);
        var files = DiscoverTests(resolvedTarget, pattern);
        if (files.Count == 0)
        {
            Console.WriteLine("[yasn:test] Тестовые файлы не найдены.");
            Console.WriteLine($"[yasn:test] target: {resolvedTarget}");
            return 1;
        }

        var passed = 0;
        var failed = 0;
        Console.WriteLine($"[yasn:test] Найдено тестов: {files.Count}");
        foreach (var file in files)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var fullPath = Path.GetFullPath(file);
                var source = File.ReadAllText(fullPath);
                var program = Pipeline.CompileSource(source, fullPath);
                using var vm = new VirtualMachine(program, fullPath);
                vm.Run();

                sw.Stop();
                passed++;
                Console.WriteLine($"[PASS] {fullPath} ({sw.ElapsedMilliseconds} ms)");
            }
            catch (Exception ex)
            {
                sw.Stop();
                failed++;
                Console.WriteLine($"[FAIL] {Path.GetFullPath(file)} ({sw.ElapsedMilliseconds} ms)");
                if (verbose)
                {
                    Console.WriteLine(ex);
                }
                else
                {
                    Console.WriteLine($"       {ex.Message}");
                }

                if (failFast)
                {
                    break;
                }
            }
        }

        startedAt.Stop();
        Console.WriteLine($"[yasn:test] PASS={passed} FAIL={failed} TOTAL={passed + failed} TIME={startedAt.ElapsedMilliseconds} ms");
        return failed == 0 ? 0 : 1;
    }

    private static string ResolveTarget(string? targetPath)
    {
        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            return Path.GetFullPath(targetPath);
        }

        var testsDir = Path.Combine(Directory.GetCurrentDirectory(), "tests");
        if (Directory.Exists(testsDir))
        {
            return testsDir;
        }

        return Directory.GetCurrentDirectory();
    }

    private static List<string> DiscoverTests(string targetPath, string? pattern)
    {
        if (File.Exists(targetPath))
        {
            return [targetPath];
        }

        if (!Directory.Exists(targetPath))
        {
            throw new YasnException($"Путь для тестов не найден: {targetPath}");
        }

        var patterns = string.IsNullOrWhiteSpace(pattern)
            ? DefaultPatterns
            : [pattern];

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePattern in patterns)
        {
            foreach (var file in Directory.EnumerateFiles(targetPath, filePattern, SearchOption.AllDirectories))
            {
                result.Add(file);
            }
        }

        return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

