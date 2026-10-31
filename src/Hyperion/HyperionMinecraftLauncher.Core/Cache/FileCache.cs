using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Platform;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Cache;

/// <summary>
/// Tiny disk-backed key-value cache used by the news client, skin fetcher, and any other
/// network-bound service. Reads check the file's last-write-time against an optional TTL;
/// writes are atomic (write-temp + rename).
/// </summary>
/// <remarks>
/// Lives under <c>%LOCALAPPDATA%\HyperionMinecraftLauncher\cache\</c> by default; tests can
/// pass an explicit directory.
/// </remarks>
public sealed class FileCache
{
    private readonly string _baseDir;

    /// <summary>Build a cache rooted at the default platform path.</summary>
    public FileCache() : this(DefaultBaseDir()) { }

    public FileCache(string baseDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDir);
        _baseDir = baseDir;
        Directory.CreateDirectory(baseDir);
    }

    /// <summary>Absolute path on disk for a given cache key.</summary>
    public string PathFor(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Path.Combine(_baseDir, Sanitize(key));
    }

    /// <summary>Returns the cached bytes if the entry exists and is not older than <paramref name="maxAge"/>.
    /// Pass <c>null</c> for "any age".</summary>
    public async Task<byte[]?> TryReadAsync(string key, TimeSpan? maxAge, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        if (!File.Exists(path)) return null;
        if (maxAge is { } ttl && (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)) > ttl) return null;

        try
        {
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Atomically write bytes to the cache (write-temp + rename).</summary>
    public async Task WriteAsync(string key, byte[] data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        var path = PathFor(key);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, data, cancellationToken).ConfigureAwait(false);
        File.Move(tmp, path, overwrite: true);
    }

    private static string Sanitize(string key)
    {
        var invalid = Path.GetInvalidFileNameChars();
        // Allow slashes as nested directories; replace any other invalid char with '_'.
        return string.Concat(key.Select(c => c == '/' || c == '\\'
            ? Path.DirectorySeparatorChar
            : invalid.Contains(c) ? '_' : c));
    }

    private static string DefaultBaseDir()
    {
        // Cache lives under $XDG_CACHE_HOME (~/.cache) on Linux, %LOCALAPPDATA% on Windows,
        // ~/Library/Application Support on macOS - whichever XdgPaths.CacheHome resolves to.
        return Path.Combine(XdgPaths.AppFolder(DefaultEnvironment.Instance, XdgCategory.Cache), "cache");
    }
}
