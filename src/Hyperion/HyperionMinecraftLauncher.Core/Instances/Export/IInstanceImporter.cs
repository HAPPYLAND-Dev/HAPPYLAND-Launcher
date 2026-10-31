using System;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Export;

/// <summary>
/// Reverse of <see cref="IInstanceExporter"/>: pulls a Hyperion-format instance zip apart,
/// extracts the game-dir contents into a fresh per-instance folder under LOCALAPPDATA, and
/// returns the rehydrated <see cref="Instance"/> record (with a freshly-generated Id so it
/// doesn't collide with anything already in the store).
/// </summary>
public interface IInstanceImporter
{
    /// <summary>
    /// Extract the zip and return the new instance record. The caller is responsible for
    /// persisting it through <c>IInstanceStore</c> / the launcher service - this method only
    /// touches the file system below the new per-instance directory.
    /// </summary>
    /// <param name="sourceZipPath">Path to the <c>.zip</c> produced by <see cref="IInstanceExporter"/>.</param>
    /// <param name="overrideName">Optional name for the new instance. Null = keep the name from the zip.</param>
    /// <param name="progress">0..1 progress reporter (extracted bytes vs. total uncompressed size).</param>
    /// <exception cref="InstanceImportException">Thrown when the zip is missing the required <c>hyperion-instance.json</c> manifest or is otherwise unreadable.</exception>
    Task<Instance> ImportAsync(
        string sourceZipPath,
        string? overrideName,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}
