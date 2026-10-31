using System;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modpacks;

/// <summary>
/// Imports a single modpack archive into a brand-new Hyperion <see cref="Instance"/>.
/// Implementations parse the archive (Modrinth <c>.mrpack</c> or CurseForge <c>.zip</c>),
/// create a fresh per-instance directory, fetch every file declared in the manifest, and
/// copy the <c>overrides/</c> tree on top before returning the built <see cref="Instance"/>
/// ready for the caller to persist.
/// </summary>
public interface IModpackImporter
{
    /// <summary>
    /// Open <paramref name="archivePath"/>, materialise the new instance folder, download
    /// every required file, copy the overrides tree, and return the <see cref="Instance"/>
    /// the caller should hand to <see cref="IInstanceStore.SaveAsync"/>.
    /// </summary>
    /// <param name="archivePath">Absolute path to the <c>.mrpack</c> or <c>.zip</c> file on disk.</param>
    /// <param name="targetInstanceName">User-chosen display name for the new instance. Falls back
    /// to the manifest's pack name when null / whitespace.</param>
    /// <param name="progress">Optional 0.0..1.0 progress sink. The importer reports overall
    /// progress as a fraction of files completed (each file's own bytes-progress is averaged in).</param>
    /// <param name="cancellationToken">Cancellation token; partial files are left on disk.</param>
    Task<Instance> ImportAsync(
        string archivePath,
        string? targetInstanceName,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}
