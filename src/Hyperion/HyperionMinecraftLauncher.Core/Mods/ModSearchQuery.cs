using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;

/// <summary>
/// Inputs to <see cref="IModRepository.SearchAsync"/>. All filters are optional - an empty
/// <see cref="Query"/> with a pinned <see cref="GameVersion"/> + <see cref="Loader"/> still
/// yields a sensible "what fits this instance" list.
/// </summary>
public sealed record ModSearchQuery
{
    /// <summary>Free-text search term. Empty = no text filter (server returns popular results).</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>Max items to return. Caps at the repository's own server-side limit.</summary>
    public int Limit { get; init; } = 20;

    /// <summary>Pagination offset (0-based).</summary>
    public int Offset { get; init; }

    /// <summary>Filter to mods that ship a file for this Minecraft version (e.g. <c>"1.20.1"</c>).</summary>
    public string? GameVersion { get; init; }

    /// <summary>Filter to mods that ship a file for this loader.</summary>
    public ModLoader? Loader { get; init; }

    /// <summary>Filter to one of the source's category tags (e.g. <c>"optimization"</c>).</summary>
    public string? Category { get; init; }
}
