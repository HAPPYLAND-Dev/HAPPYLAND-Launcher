using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Backups;

/// <summary>
/// Per-instance world backup contract - Prism's safety net for the "MultiMC ate my world"
/// class of incident. All archives live under <c>&lt;gameDir&gt;/backups/</c> with the file
/// naming convention <c>{worldName}-{yyyyMMdd-HHmmss}.zip</c> so listing + pruning is a
/// pure filename operation (no sidecar metadata to keep in sync).
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Zip every subdirectory of <c>&lt;gameDir&gt;/saves/</c> into its own archive under
    /// <c>&lt;gameDir&gt;/backups/</c>. Streamed so we don't materialise full worlds in RAM.
    /// Skips empty subdirectories. Returns the entries actually written (newest first).
    /// </summary>
    Task<IReadOnlyList<BackupEntry>> BackupAllWorldsAsync(Instance instance, CancellationToken cancellationToken);

    /// <summary>
    /// Zip one specific world subdirectory (<c>&lt;gameDir&gt;/saves/{worldFolderName}</c>)
    /// into a single archive. Returns null when the folder does not exist or is empty.
    /// </summary>
    Task<BackupEntry?> BackupWorldAsync(Instance instance, string worldFolderName, CancellationToken cancellationToken);

    /// <summary>
    /// List every backup zip under <c>&lt;gameDir&gt;/backups/</c>, newest first.
    /// Files that do not match the naming pattern are surfaced anyway with the file's
    /// <c>LastWriteTimeUtc</c> as the timestamp and the bare filename as the world name.
    /// </summary>
    Task<IReadOnlyList<BackupEntry>> ListBackupsAsync(Instance instance, CancellationToken cancellationToken);

    /// <summary>
    /// Extract <paramref name="entry"/>'s archive back into the instance saves directory
    /// at <c>&lt;gameDir&gt;/saves/{worldName}-restored-{ts}/</c> so the original world
    /// folder is never overwritten.
    /// </summary>
    Task RestoreAsync(BackupEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// Keep the <paramref name="keepLatest"/> newest backups per world (grouped by
    /// <see cref="BackupEntry.SourceWorldName"/>) and delete every older archive.
    /// A <paramref name="keepLatest"/> of 0 or negative is treated as "delete nothing".
    /// </summary>
    Task PruneAsync(Instance instance, int keepLatest, CancellationToken cancellationToken);
}
