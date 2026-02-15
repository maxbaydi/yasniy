using System.IO.Compression;

namespace YasnNative.App;

public sealed record UiAsset(string Path, byte[] Content, string ContentType);

public sealed class UiAssetManifest
{
    private readonly Dictionary<string, UiAsset> _assets;

    private UiAssetManifest(Dictionary<string, UiAsset> assets)
    {
        _assets = assets;
    }

    public bool HasAssets => _assets.Count > 0;

    public static byte[] PackDirectory(string distDirectory)
    {
        var fullDist = Path.GetFullPath(distDirectory);
        if (!Directory.Exists(fullDist))
        {
            throw new DirectoryNotFoundException($"UI dist directory not found: {fullDist}");
        }

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var files = Directory.EnumerateFiles(fullDist, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(fullDist, file)
                    .Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(relative))
                {
                    continue;
                }

                var entry = zip.CreateEntry(relative, CompressionLevel.SmallestSize);
                using var input = File.OpenRead(file);
                using var output = entry.Open();
                input.CopyTo(output);
            }
        }

        return stream.ToArray();
    }

    public static UiAssetManifest FromZip(byte[] zipBytes)
    {
        using var stream = new MemoryStream(zipBytes, writable: false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        var assets = new Dictionary<string, UiAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            var normalized = NormalizePath(entry.FullName);
            if (normalized is null)
            {
                continue;
            }

            using var entryStream = entry.Open();
            using var output = new MemoryStream();
            entryStream.CopyTo(output);
            var bytes = output.ToArray();
            assets[normalized] = new UiAsset(
                normalized,
                bytes,
                GuessContentType(normalized));
        }

        return new UiAssetManifest(assets);
    }

    public bool TryResolve(string requestPath, out UiAsset asset)
    {
        var normalized = NormalizePath(requestPath) ?? "/";
        if (_assets.TryGetValue(normalized, out asset!))
        {
            return true;
        }

        if (normalized == "/" && _assets.TryGetValue("/index.html", out asset!))
        {
            return true;
        }

        if (!Path.HasExtension(normalized) && _assets.TryGetValue("/index.html", out asset!))
        {
            return true;
        }

        asset = null!;
        return false;
    }

    private static string? NormalizePath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "/";
        }

        var path = raw
            .Replace('\\', '/')
            .Split('?', 2)[0]
            .Trim();
        if (path.Length == 0)
        {
            return "/";
        }

        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            path = "/" + path;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(seg => seg is "." or ".."))
        {
            return null;
        }

        return "/" + string.Join("/", segments);
    }

    private static string GuessContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".mjs" => "application/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream",
        };
    }
}
