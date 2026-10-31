using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Versions;

/// <summary>
/// Lightweight description of a Minecraft version, suitable for binding to a UI list.
/// Stable, independent of CmlLib's own metadata type.
/// </summary>
public sealed record VersionMetadata
{
    /// <summary>The version id as understood by the Minecraft launcher manifest (e.g. <c>"1.21.5"</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Version type — <c>"release"</c>, <c>"snapshot"</c>, <c>"old_beta"</c>, etc.</summary>
    public required string Type { get; init; }

    /// <summary>The official release timestamp; default if unknown.</summary>
    public DateTimeOffset ReleaseTime { get; init; }

    /// <summary>One-line label for ComboBox / ListBox display.</summary>
    public string DisplayText => string.IsNullOrEmpty(Type) ? Name : $"{Name}  -  {Type}";
}
