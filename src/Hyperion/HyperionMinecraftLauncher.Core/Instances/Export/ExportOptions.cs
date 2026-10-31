namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Export;

/// <summary>
/// Knobs for <see cref="IInstanceExporter.ExportAsync(Instance, string, ExportOptions, System.IProgress{double}?, System.Threading.CancellationToken)"/>.
/// Defaults exclude personal / bulky data (worlds, screenshots) so a shared zip can be safely
/// handed to a friend without leaking the user's save files.
/// </summary>
/// <param name="IncludeSaves">When true, copy the instance's <c>saves/</c> folder into the zip.</param>
/// <param name="IncludeScreenshots">When true, copy the instance's <c>screenshots/</c> folder into the zip.</param>
/// <param name="IncludeWorldData">
/// Reserved knob mirroring Prism's "include world data" toggle. Today it has the same effect as
/// <see cref="IncludeSaves"/>; kept separate so we can split out third-party world plugins
/// (Worldedit schematics, etc.) without changing call sites later.
/// </param>
public sealed record ExportOptions(
    bool IncludeSaves = false,
    bool IncludeScreenshots = false,
    bool IncludeWorldData = false)
{
    /// <summary>Hand a copy of the safe defaults (everything excluded except mods/configs/resourcepacks).</summary>
    public static ExportOptions Default { get; } = new();
}
