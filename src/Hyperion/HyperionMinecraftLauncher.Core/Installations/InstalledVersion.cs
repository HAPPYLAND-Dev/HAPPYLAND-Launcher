using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;

/// <summary>
/// A Minecraft version present on the local disk (under <c>.minecraft/versions/&lt;id&gt;/</c>).
/// Carries enough metadata to render a launcher list-item and to feed CmlLib's
/// installer / launcher with the correct <c>id</c>.
/// </summary>
public sealed record InstalledVersion
{
    /// <summary>Folder name and version id (e.g. <c>"1.21.5"</c>, <c>"1.20.1-forge-47.4.5"</c>).</summary>
    public required string Id { get; init; }

    /// <summary>From the manifest's <c>type</c> field: <c>"release"</c>, <c>"snapshot"</c>, <c>"old_beta"</c>, <c>"old_alpha"</c>; <c>"custom"</c> when absent or non-standard (mod loaders).</summary>
    public required string Type { get; init; }

    /// <summary>From the manifest's <c>releaseTime</c> field if present and parsable; <c>null</c> otherwise.</summary>
    public DateTimeOffset? ReleaseTime { get; init; }

    /// <summary>Absolute path to <c>versions/&lt;id&gt;/&lt;id&gt;.json</c>.</summary>
    public required string JsonPath { get; init; }

    /// <summary>Absolute path to <c>versions/&lt;id&gt;/&lt;id&gt;.jar</c> when it exists. Mod-loader manifests often omit the jar (they inherit from the parent vanilla).</summary>
    public string? JarPath { get; init; }

    /// <summary>Detected mod loader.</summary>
    public ModLoader Loader { get; init; } = ModLoader.None;

    /// <summary>From the manifest's <c>inheritsFrom</c> field. Always set for Forge/NeoForge/Fabric/Quilt.</summary>
    public string? ParentVersionId { get; init; }

    /// <summary>One-line label for ComboBox / ListBox display. Avoids the
    /// <c>&lt;Run&gt;</c>-binding pitfall in Avalonia 11 where the selected-item
    /// presenter would render an empty string from a compound template.</summary>
    public string DisplayText => Loader == ModLoader.None ? Id : $"{Id}  -  {Loader}";
}
