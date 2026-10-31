using System;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Import.MultiMc;

/// <summary>
/// Imports a MultiMC / Prism Launcher instance into Hyperion as a first-class
/// <see cref="Instance"/>. Accepts either a shared <c>.zip</c> (the format Prism's
/// "Export instance" produces) or a copy of the on-disk instance folder. Throws an
/// <see cref="Export.InstanceImportException"/> with a friendly message when the source
/// is missing <c>instance.cfg</c> / <c>mmc-pack.json</c> or the files are malformed.
/// </summary>
public interface IMultiMcInstanceImporter
{
    /// <summary>
    /// Open <paramref name="sourceZipOrFolderPath"/> (zip file or unzipped folder), pull
    /// the Prism settings + component list out, copy the <c>.minecraft</c> tree into a
    /// fresh per-instance folder under Hyperion's data root, and return the built
    /// <see cref="Instance"/> record. The caller is responsible for persisting it through
    /// <see cref="IInstanceStore"/>.
    /// </summary>
    /// <param name="sourceZipOrFolderPath">Absolute path to the Prism <c>.zip</c> or the unzipped instance folder.</param>
    /// <param name="overrideName">Optional Hyperion-side name. Null = keep the cfg's <c>name=</c>.</param>
    /// <param name="progress">0..1 progress reporter (extracted bytes vs. total uncompressed size; folder imports report 0.0/1.0 only).</param>
    /// <param name="cancellationToken">Standard cancellation; partial extractions may be left behind.</param>
    Task<Instance> ImportAsync(
        string sourceZipOrFolderPath,
        string? overrideName,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}
