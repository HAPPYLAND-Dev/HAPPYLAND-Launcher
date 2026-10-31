using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing.Packs;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing;

/// <summary>
/// File-system-backed <see cref="IInstanceBrowser"/>. Resolves the per-instance root to
/// <see cref="Instance.GameDirectory"/> when set, otherwise the platform default <c>.minecraft</c>.
/// Each list method runs the I/O on the thread pool so callers can hand us a CancellationToken
/// without blocking the UI thread on the disk scan.
/// </summary>
public sealed class FileSystemInstanceBrowser : IInstanceBrowser
{
    private readonly IServersStore _serversStore;

    /// <summary>Default ctor - wraps a plain <see cref="FileServersStore"/> for <c>servers.dat</c> reads.</summary>
    public FileSystemInstanceBrowser() : this(new FileServersStore())
    {
    }

    /// <summary>Test-friendly ctor: inject a fake servers store.</summary>
    public FileSystemInstanceBrowser(IServersStore serversStore)
    {
        _serversStore = serversStore ?? throw new ArgumentNullException(nameof(serversStore));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScreenshotEntry>> ListScreenshotsAsync(Instance instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var root = ResolveRoot(instance);
        var dir = Path.Combine(root, "screenshots");

        return Task.Run<IReadOnlyList<ScreenshotEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(dir)) return Array.Empty<ScreenshotEntry>();

            var entries = new List<ScreenshotEntry>();
            foreach (var path in Directory.EnumerateFiles(dir, "*.png", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(path);
                    PngIhdrReader.TryReadSize(path, out var w, out var h);
                    entries.Add(new ScreenshotEntry
                    {
                        FullPath = info.FullName,
                        Filename = info.Name,
                        TakenAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                        SizeBytes = info.Length,
                        Width = w,
                        Height = h,
                    });
                }
                catch
                {
                    // Skip unreadable files - never crash the whole list because one PNG is bad.
                }
            }

            return entries
                .OrderByDescending(e => e.TakenAt)
                .ToArray();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WorldEntry>> ListWorldsAsync(Instance instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var root = ResolveRoot(instance);
        var savesDir = Path.Combine(root, "saves");

        return Task.Run<IReadOnlyList<WorldEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(savesDir)) return Array.Empty<WorldEntry>();

            var entries = new List<WorldEntry>();
            foreach (var dir in Directory.EnumerateDirectories(savesDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var levelDatPath = Path.Combine(dir, "level.dat");
                if (!File.Exists(levelDatPath)) continue;

                var folderName = Path.GetFileName(dir);
                var info = LevelDatReader.Read(levelDatPath);
                long size = TryComputeDirectorySize(dir);

                entries.Add(new WorldEntry
                {
                    FullPath = dir,
                    FolderName = folderName,
                    DisplayName = info?.LevelName ?? folderName,
                    LastPlayedIso = info?.LastPlayedIso,
                    SizeBytes = size,
                    GameMode = info?.GameType,
                    Version = info?.VersionName,
                });
            }

            return entries
                .OrderByDescending(e => e.LastPlayedIso ?? string.Empty)
                .ToArray();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ServerListEntry>> ListServersAsync(Instance instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var root = ResolveRoot(instance);
        var path = Path.Combine(root, "servers.dat");
        return _serversStore.LoadAsync(path, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ResourcePackEntry>> ListResourcePacksAsync(Instance instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var dir = Path.Combine(ResolveRoot(instance), "resourcepacks");

        return Task.Run<IReadOnlyList<ResourcePackEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(dir)) return Array.Empty<ResourcePackEntry>();

            var entries = new List<ResourcePackEntry>();
            foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsZipOrDisabled(path, out bool enabled)) continue;
                try
                {
                    var info = new FileInfo(path);
                    var meta = PackMetadataReader.Read(info.FullName, PackKind.Resource);
                    entries.Add(new ResourcePackEntry
                    {
                        FullPath = info.FullName,
                        Filename = info.Name,
                        SizeBytes = info.Length,
                        IsEnabled = enabled,
                        IconPng = meta.IconPng,
                        Description = meta.Description,
                    });
                }
                catch
                {
                    // Skip unreadable entries - never crash the whole list.
                }
            }

            return entries
                .OrderBy(e => e.Filename, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ShaderPackEntry>> ListShaderPacksAsync(Instance instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var dir = Path.Combine(ResolveRoot(instance), "shaderpacks");

        return Task.Run<IReadOnlyList<ShaderPackEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(dir)) return Array.Empty<ShaderPackEntry>();

            var entries = new List<ShaderPackEntry>();
            foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsZipOrDisabled(path, out bool enabled)) continue;
                try
                {
                    var info = new FileInfo(path);
                    var meta = PackMetadataReader.Read(info.FullName, PackKind.Shader);
                    entries.Add(new ShaderPackEntry
                    {
                        FullPath = info.FullName,
                        Filename = info.Name,
                        SizeBytes = info.Length,
                        IsEnabled = enabled,
                        IconPng = meta.IconPng,
                        Description = meta.Description,
                    });
                }
                catch
                {
                    // Skip unreadable entries.
                }
            }

            return entries
                .OrderBy(e => e.Filename, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DataPackEntry>> ListDataPacksAsync(Instance instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var savesDir = Path.Combine(ResolveRoot(instance), "saves");

        return Task.Run<IReadOnlyList<DataPackEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(savesDir)) return Array.Empty<DataPackEntry>();

            var entries = new List<DataPackEntry>();
            foreach (var worldDir in Directory.EnumerateDirectories(savesDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var packDir = Path.Combine(worldDir, "datapacks");
                if (!Directory.Exists(packDir)) continue;
                var worldName = Path.GetFileName(worldDir);

                foreach (var path in Directory.EnumerateFiles(packDir, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsZipOrDisabled(path, out bool enabled)) continue;
                    try
                    {
                        var info = new FileInfo(path);
                        var meta = PackMetadataReader.Read(info.FullName, PackKind.Data);
                        entries.Add(new DataPackEntry
                        {
                            FullPath = info.FullName,
                            Filename = info.Name,
                            SizeBytes = info.Length,
                            WorldFolderName = worldName,
                            IsEnabled = enabled,
                            IconPng = meta.IconPng,
                            Description = meta.Description,
                        });
                    }
                    catch
                    {
                        // Skip unreadable entries.
                    }
                }
            }

            return entries
                .OrderBy(e => e.WorldFolderName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Filename, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }, cancellationToken);
    }

    /// <summary>
    /// Treat <c>.zip</c> as enabled, <c>.zip.disabled</c> as disabled, everything else as ignored.
    /// Tightening this to a positive whitelist avoids picking up random files (READMEs, .DS_Store)
    /// in pack directories.
    /// </summary>
    private static bool IsZipOrDisabled(string path, out bool isEnabled)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            isEnabled = true;
            return true;
        }
        if (path.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase))
        {
            isEnabled = false;
            return true;
        }
        isEnabled = false;
        return false;
    }

    private static string ResolveRoot(Instance instance)
    {
        return !string.IsNullOrWhiteSpace(instance.GameDirectory)
            ? instance.GameDirectory
            : DefaultMinecraftInstallationLocator.ResolveRoot();
    }

    private static long TryComputeDirectorySize(string dir)
    {
        try
        {
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; }
                catch { /* skip unreadable file */ }
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }
}
