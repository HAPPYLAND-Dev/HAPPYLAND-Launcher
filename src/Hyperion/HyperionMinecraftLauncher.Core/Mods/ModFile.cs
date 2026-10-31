using System.Collections.Generic;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;

/// <summary>
/// One downloadable artifact under a <see cref="Mod"/> - the (game version, loader) tuple
/// determines which file the launcher picks for a given <see cref="Instances.Instance"/>.
/// </summary>
public sealed record ModFile
{
    /// <summary>Parent <see cref="Mod.Id"/>.</summary>
    public required string ModId { get; init; }

    /// <summary>Repository-native id of this version (Modrinth: <c>id</c>; CurseForge: <c>fileId</c>).</summary>
    public required string FileId { get; init; }

    /// <summary>Display label (typically the mod version, e.g. <c>"0.92.2+1.20.1"</c>).</summary>
    public required string DisplayName { get; init; }

    /// <summary>Original filename to write under <c>mods/</c>. Null = derive from the URL.</summary>
    public string? Filename { get; init; }

    /// <summary>Direct download URL for the .jar artifact.</summary>
    public required string DownloadUrl { get; init; }

    /// <summary>Minecraft versions this file targets (e.g. <c>["1.20.1","1.20"]</c>).</summary>
    public IReadOnlyList<string> GameVersions { get; init; } = System.Array.Empty<string>();

    /// <summary>Loaders this file targets. Empty for resource packs / data packs.</summary>
    public IReadOnlyList<ModLoader> Loaders { get; init; } = System.Array.Empty<ModLoader>();

    /// <summary>File size in bytes.</summary>
    public long FileSize { get; init; }

    /// <summary>Sha-1 hash for verifying the download (when the repository ships one).</summary>
    public string? Sha1 { get; init; }
}
