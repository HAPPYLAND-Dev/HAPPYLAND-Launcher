using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Backups;

/// <summary>
/// One on-disk backup zip produced by <see cref="IBackupService"/>. Records the absolute
/// archive path, when it was created (parsed from the filename timestamp), the file size
/// in bytes for UI display, and the world folder name the backup originated from.
/// </summary>
public sealed record BackupEntry
{
    /// <summary>Absolute path to the zip file under <c>&lt;gameDir&gt;/backups/</c>.</summary>
    public required string ArchivePath { get; init; }

    /// <summary>UTC timestamp parsed from the filename suffix (<c>yyyyMMdd-HHmmss</c>).
    /// Falls back to the file's <c>LastWriteTimeUtc</c> when the suffix is unparseable.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Compressed zip size in bytes (the file length on disk).</summary>
    public required long SizeBytes { get; init; }

    /// <summary>The original world folder name this archive was zipped from
    /// (parsed off the filename prefix, before the timestamp).</summary>
    public required string SourceWorldName { get; init; }
}
