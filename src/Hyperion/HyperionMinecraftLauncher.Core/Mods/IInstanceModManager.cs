using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;

/// <summary>
/// Manages the mods folder of a given <see cref="Instance"/>: list / install / enable / remove.
/// The implementation owns the on-disk layout - callers stay in terms of <see cref="ModFile"/>
/// and <see cref="LocalMod"/>.
/// </summary>
public interface IInstanceModManager
{
    /// <summary>
    /// Enumerate every <c>.jar</c> and <c>.jar.disabled</c> in the instance's mods folder.
    /// Returns an empty list when the folder doesn't exist yet.
    /// </summary>
    Task<IReadOnlyList<LocalMod>> ListInstalledAsync(Instance instance, CancellationToken cancellationToken);

    /// <summary>Download <paramref name="file"/> via <paramref name="repository"/> and write it to the instance's <c>mods/</c> folder.</summary>
    Task InstallAsync(Instance instance, ModFile file, IModRepository repository, CancellationToken cancellationToken);

    /// <summary>Delete the named file (with or without the <c>.disabled</c> suffix).</summary>
    Task RemoveAsync(Instance instance, string filename, CancellationToken cancellationToken);

    /// <summary>
    /// Toggle a mod: when <paramref name="enabled"/>=true and the file ends in <c>.jar.disabled</c>
    /// the suffix is stripped; when false a plain <c>.jar</c> gets <c>.disabled</c> appended.
    /// No-op when the file is already in the requested state.
    /// </summary>
    Task SetEnabledAsync(Instance instance, string filename, bool enabled, CancellationToken cancellationToken);
}
