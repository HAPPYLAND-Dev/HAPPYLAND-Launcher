using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.History;

/// <summary>
/// One entry in the skin-history rotation. The PNG itself lives on disk at
/// <see cref="FilePath"/>; this record is what the UI binds to in the gallery.
/// </summary>
public sealed record SkinHistoryEntry
{
    /// <summary>Stable id (GUID, no dashes). Doubles as the on-disk file name.</summary>
    public required string Id { get; init; }

    /// <summary>Absolute path of the PNG on disk.</summary>
    public required string FilePath { get; init; }

    /// <summary>Variant the skin was uploaded with (classic / slim).</summary>
    public required SkinVariant Variant { get; init; }

    /// <summary>When this entry was appended (UTC).</summary>
    public required DateTimeOffset SavedAt { get; init; }
}
