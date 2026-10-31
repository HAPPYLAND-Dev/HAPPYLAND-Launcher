using System;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Export;

/// <summary>
/// Bundles a Hyperion instance (its <see cref="Instance"/> record plus the relevant slice of
/// the game directory) into a single <c>.zip</c> file. The output layout follows the same
/// "Hyperion instance" convention <see cref="IInstanceImporter"/> consumes:
///
/// <code>
///   hyperion-instance.json      &lt;-- the Instance record (replaces FileInstanceStore JSON)
///   metadata.json               &lt;-- { FormatVersion="1", ExporterVersion, ExportedAt }
///   gameDir/...                 &lt;-- flat copy of the instance's game dir, filtered by ExportOptions
/// </code>
///
/// Inspired by Prism / MultiMC's <c>.zip</c> share format, but Hyperion's own metadata model
/// rather than a Prism config (so we don't have to lossy-translate every knob).
/// </summary>
public interface IInstanceExporter
{
    /// <summary>Export the instance to <paramref name="destinationZipPath"/> using the safe defaults (saves/screenshots excluded).</summary>
    Task ExportAsync(
        Instance instance,
        string destinationZipPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken);

    /// <summary>Export with explicit <paramref name="options"/> controlling what gets bundled.</summary>
    Task ExportAsync(
        Instance instance,
        string destinationZipPath,
        ExportOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}
