using System.Collections.Generic;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;

/// <summary>
/// A mod listing returned by a remote repository (Modrinth / CurseForge). One <see cref="Mod"/>
/// can ship many <see cref="ModFile"/>s - one per (game version, loader) pair. The properties
/// here are the minimum a tile / card needs to render.
/// </summary>
public sealed record Mod
{
    /// <summary>Repository-native id (Modrinth: opaque slug-id, CurseForge: integer as string).</summary>
    public required string Id { get; init; }

    /// <summary>Stable URL-safe handle (Modrinth: <c>slug</c>; CurseForge: <c>slug</c>).</summary>
    public required string Slug { get; init; }

    /// <summary>Display title (e.g. <c>"Fabric API"</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Short description shown under the title in the search-results list.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Author handle as displayed on the source site (CurseForge calls them "primary author").</summary>
    public string AuthorDisplay { get; init; } = string.Empty;

    /// <summary>Absolute URL of the project icon, when the repository ships one.</summary>
    public string? IconUri { get; init; }

    /// <summary>Absolute URL of the mod's project page on the source site.</summary>
    public string? PageUri { get; init; }

    /// <summary>Tag-style categories (e.g. <c>["library","optimization"]</c>).</summary>
    public IReadOnlyList<string> Categories { get; init; } = System.Array.Empty<string>();

    /// <summary>Lifetime download count as reported by the source. Used to sort + show a chip.</summary>
    public long Downloads { get; init; }

    /// <summary>Which repository produced this listing.</summary>
    public required ModSource Source { get; init; }
}
