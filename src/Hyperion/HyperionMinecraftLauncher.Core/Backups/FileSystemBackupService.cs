using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Backups;

/// <summary>
/// File-system-backed <see cref="IBackupService"/>. Resolves the per-instance root the same
/// way <c>FileSystemInstanceBrowser</c> does (instance override > platform default), then
/// streams each world subdirectory through <see cref="ZipFile.CreateFromDirectory(string, string)"/>
/// so memory pressure stays flat even on multi-gigabyte worlds. The filename pattern
/// <c>{worldName}-{yyyyMMdd-HHmmss}.zip</c> is the single source of truth - list / prune /
/// restore are all pure filename operations.
/// </summary>
public sealed class FileSystemBackupService : IBackupService
{
    /// <summary>The timestamp format suffixed after each backup's world name.</summary>
    public const string TimestampFormat = "yyyyMMdd-HHmmss";

    private const string BackupsSubdir = "backups";
    private const string SavesSubdir = "saves";

    private readonly Func<DateTimeOffset> _now;

    /// <summary>Production ctor: uses <see cref="DateTimeOffset.UtcNow"/> for timestamps.</summary>
    public FileSystemBackupService() : this(() => DateTimeOffset.UtcNow)
    {
    }

    /// <summary>Test-friendly ctor: inject a fake clock so generated filenames are deterministic.</summary>
    public FileSystemBackupService(Func<DateTimeOffset> now)
    {
        _now = now ?? throw new ArgumentNullException(nameof(now));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BackupEntry>> BackupAllWorldsAsync(Instance instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var root = ResolveRoot(instance);
        var savesDir = Path.Combine(root, SavesSubdir);
        var backupsDir = Path.Combine(root, BackupsSubdir);

        return Task.Run<IReadOnlyList<BackupEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(savesDir)) return Array.Empty<BackupEntry>();

            Directory.CreateDirectory(backupsDir);

            var entries = new List<BackupEntry>();
            foreach (var worldDir in Directory.EnumerateDirectories(savesDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsEffectivelyEmpty(worldDir)) continue;
                var entry = ZipOneWorld(worldDir, backupsDir);
                if (entry is not null) entries.Add(entry);
            }
            return entries
                .OrderByDescending(e => e.CreatedAt)
                .ToArray();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<BackupEntry?> BackupWorldAsync(Instance instance, string worldFolderName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldFolderName);

        var root = ResolveRoot(instance);
        var worldDir = Path.Combine(root, SavesSubdir, worldFolderName);
        var backupsDir = Path.Combine(root, BackupsSubdir);

        return Task.Run<BackupEntry?>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(worldDir)) return null;
            if (IsEffectivelyEmpty(worldDir)) return null;
            Directory.CreateDirectory(backupsDir);
            return ZipOneWorld(worldDir, backupsDir);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BackupEntry>> ListBackupsAsync(Instance instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var root = ResolveRoot(instance);
        var backupsDir = Path.Combine(root, BackupsSubdir);

        return Task.Run<IReadOnlyList<BackupEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(backupsDir)) return Array.Empty<BackupEntry>();

            var entries = new List<BackupEntry>();
            foreach (var path in Directory.EnumerateFiles(backupsDir, "*.zip", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(path);
                    var (worldName, createdAt) = ParseFilename(info.Name, info.LastWriteTimeUtc);
                    entries.Add(new BackupEntry
                    {
                        ArchivePath = info.FullName,
                        CreatedAt = createdAt,
                        SizeBytes = info.Length,
                        SourceWorldName = worldName,
                    });
                }
                catch
                {
                    // Skip unreadable entries - one bad file should never break the list.
                }
            }
            return entries
                .OrderByDescending(e => e.CreatedAt)
                .ToArray();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task RestoreAsync(BackupEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.ArchivePath);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(entry.ArchivePath))
                throw new FileNotFoundException("Backup archive missing.", entry.ArchivePath);

            // The archive lives at <gameDir>/backups/<file>.zip - go two levels up to find the
            // gameDir, then write the restored world next to the original under saves/.
            var backupsDir = Path.GetDirectoryName(entry.ArchivePath)!;
            var gameDir = Path.GetDirectoryName(backupsDir)!;
            var savesDir = Path.Combine(gameDir, SavesSubdir);
            Directory.CreateDirectory(savesDir);

            var ts = _now().ToString(TimestampFormat, CultureInfo.InvariantCulture);
            var restoredDir = Path.Combine(savesDir, $"{entry.SourceWorldName}-restored-{ts}");
            // Guarantee uniqueness even if two restores fire inside the same second.
            int suffix = 1;
            var candidate = restoredDir;
            while (Directory.Exists(candidate))
            {
                candidate = restoredDir + "-" + suffix;
                suffix++;
            }
            Directory.CreateDirectory(candidate);

            ZipFile.ExtractToDirectory(entry.ArchivePath, candidate, overwriteFiles: false);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task PruneAsync(Instance instance, int keepLatest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (keepLatest <= 0) return;

        var all = await ListBackupsAsync(instance, cancellationToken).ConfigureAwait(false);
        if (all.Count == 0) return;

        await Task.Run(() =>
        {
            foreach (var group in all.GroupBy(e => e.SourceWorldName, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ordered = group.OrderByDescending(e => e.CreatedAt).ToList();
                for (int i = keepLatest; i < ordered.Count; i++)
                {
                    try { File.Delete(ordered[i].ArchivePath); }
                    catch { /* skip locked / unreadable - we'll catch it on the next prune. */ }
                }
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private BackupEntry? ZipOneWorld(string worldDir, string backupsDir)
    {
        var worldName = Path.GetFileName(worldDir);
        var ts = _now().ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var archive = Path.Combine(backupsDir, $"{worldName}-{ts}.zip");
        // If a backup already exists for this exact second (re-run), suffix it so we never overwrite.
        int suffix = 1;
        while (File.Exists(archive))
        {
            archive = Path.Combine(backupsDir, $"{worldName}-{ts}-{suffix}.zip");
            suffix++;
        }

        try
        {
            ZipFile.CreateFromDirectory(worldDir, archive, CompressionLevel.Optimal, includeBaseDirectory: true);
        }
        catch
        {
            // Couldn't write - leave behind no half-written file.
            try { if (File.Exists(archive)) File.Delete(archive); } catch { }
            return null;
        }

        var info = new FileInfo(archive);
        return new BackupEntry
        {
            ArchivePath = info.FullName,
            CreatedAt = _now(),
            SizeBytes = info.Length,
            SourceWorldName = worldName,
        };
    }

    private static bool IsEffectivelyEmpty(string dir)
    {
        try
        {
            using var e = Directory.EnumerateFileSystemEntries(dir).GetEnumerator();
            return !e.MoveNext();
        }
        catch
        {
            return true;
        }
    }

    private static string ResolveRoot(Instance instance)
    {
        return !string.IsNullOrWhiteSpace(instance.GameDirectory)
            ? instance.GameDirectory!
            : DefaultMinecraftInstallationLocator.ResolveRoot();
    }

    /// <summary>
    /// Parse <c>{worldName}-{yyyyMMdd-HHmmss}.zip</c> back into its components. Returns
    /// the file's <see cref="DateTime.LastWriteTimeUtc"/> as a fallback when the suffix
    /// doesn't parse - we never crash on a hand-renamed file.
    /// </summary>
    internal static (string worldName, DateTimeOffset createdAt) ParseFilename(string filename, DateTime fileMtimeUtc)
    {
        var stem = Path.GetFileNameWithoutExtension(filename);

        // Find the timestamp suffix: it's the last token after a dash that parses as the format.
        var idx = stem.LastIndexOf('-', stem.Length - 1);
        while (idx > 0)
        {
            // Walk back to find the start of the timestamp (yyyyMMdd-HHmmss has a dash in the middle).
            var prevDash = stem.LastIndexOf('-', idx - 1);
            if (prevDash <= 0) break;

            var candidate = stem.Substring(prevDash + 1);
            if (DateTimeOffset.TryParseExact(
                    candidate,
                    TimestampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return (stem.Substring(0, prevDash), parsed);
            }

            // Handle the "-<digit>" disambiguation suffix produced by ZipOneWorld when two backups
            // share the same second: strip it and re-attempt parsing on the prefix.
            if (int.TryParse(stem.Substring(idx + 1), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                idx = prevDash;
                continue;
            }
            break;
        }

        return (stem, new DateTimeOffset(DateTime.SpecifyKind(fileMtimeUtc, DateTimeKind.Utc), TimeSpan.Zero));
    }
}
