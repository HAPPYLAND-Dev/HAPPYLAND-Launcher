using System.Collections.Generic;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modpacks;

/// <summary>
/// Format-agnostic snapshot of a parsed modpack archive (Modrinth <c>.mrpack</c> or
/// CurseForge <c>.zip</c>). The importer builds one of these from the archive's
/// manifest file, then drives the download + override-copy stages from the
/// flattened <see cref="Files"/> list. <see cref="OverridesPath"/> is the
/// inside-archive directory whose tree gets copied verbatim into the new instance
/// folder (typically <c>overrides/</c>, sometimes <c>client-overrides/</c>).
/// </summary>
public sealed record ModpackManifest
{
    /// <summary>Display name of the modpack (e.g. <c>"Cobblemon"</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Pack version string (e.g. <c>"1.4.2"</c>). Empty when the manifest omits it.</summary>
    public required string Version { get; init; }

    /// <summary>Minecraft version the pack targets (e.g. <c>"1.20.1"</c>).</summary>
    public required string MinecraftVersion { get; init; }

    /// <summary>Mod loader required by the pack. <see cref="ModLoader.None"/> for vanilla data packs.</summary>
    public required ModLoader Loader { get; init; }

    /// <summary>Loader version string (e.g. <c>"0.16.5"</c>); null = pack didn't specify a pin.</summary>
    public string? LoaderVersion { get; init; }

    /// <summary>Files to fetch from the network and place inside the instance directory.</summary>
    public required IReadOnlyList<ModpackFile> Files { get; init; }

    /// <summary>Inside-archive path of the overrides tree (e.g. <c>"overrides"</c>). Null = no overrides.</summary>
    public string? OverridesPath { get; init; }
}
