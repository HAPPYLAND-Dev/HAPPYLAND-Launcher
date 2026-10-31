using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Export;

/// <summary>
/// Sidecar file written alongside <c>hyperion-instance.json</c> at the root of the export zip.
/// Lets future versions of the importer recognise the zip and react to producer/timestamp info
/// (e.g. refuse to import a zip with an unsupported FormatVersion).
/// </summary>
/// <param name="FormatVersion">Schema version of the zip layout. Currently <c>"1"</c>.</param>
/// <param name="ExporterVersion">Launcher version that produced the zip (matches <c>&lt;Version&gt;</c> in csproj).</param>
/// <param name="ExportedAt">UTC timestamp the export was written.</param>
public sealed record InstanceExportMetadata(
    string FormatVersion,
    string ExporterVersion,
    DateTimeOffset ExportedAt)
{
    /// <summary>Current schema version. Bumped if the zip layout ever changes.</summary>
    public const string CurrentFormatVersion = "1";
}
