using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;

/// <summary>
/// Heuristic: classify an installed version as Forge / Fabric / Quilt / etc. from
/// (a) its <c>mainClass</c>, (b) the presence and value of <c>inheritsFrom</c>, and
/// (c) the folder / id naming conventions used by each loader's installer.
/// Order matters - more specific tests run first.
/// </summary>
public static class LoaderDetector
{
    /// <summary>
    /// Returns the most likely <see cref="ModLoader"/> for the given manifest fields.
    /// Vanilla returns <see cref="ModLoader.None"/>.
    /// </summary>
    public static ModLoader Detect(string id, string mainClass, string? inheritsFrom)
    {
        // 1) mainClass package-prefix tests - cheapest and most reliable.
        if (StartsWithCI(mainClass, "net.fabricmc."))
            return ModLoader.Fabric;
        if (StartsWithCI(mainClass, "org.quiltmc."))
            return ModLoader.Quilt;
        if (StartsWithCI(mainClass, "cpw.mods.bootstraplauncher.")
            || StartsWithCI(mainClass, "cpw.mods.modlauncher."))
        {
            // Both Forge (1.13+) and NeoForge use this entry point. Disambiguate by id.
            return ContainsCI(id, "neoforge") ? ModLoader.NeoForge : ModLoader.Forge;
        }
        if (StartsWithCI(mainClass, "cpw.mods.fml."))
            return ModLoader.LegacyForge;
        if (StartsWithCI(mainClass, "net.minecraft.launchwrapper.")
            && ContainsCI(id, "optifine"))
        {
            return ModLoader.OptiFine;
        }

        // 2) Folder/id naming conventions: useful when mainClass is missing or generic.
        if (ContainsCI(id, "neoforge"))
            return ModLoader.NeoForge;
        if (ContainsCI(id, "fabric-loader"))
            return ModLoader.Fabric;
        if (ContainsCI(id, "quilt-loader"))
            return ModLoader.Quilt;
        if (ContainsCI(id, "forge"))
            return ModLoader.Forge;
        if (ContainsCI(id, "optifine"))
            return ModLoader.OptiFine;

        // 3) Last resort: inheritsFrom alone marks a non-vanilla install of unknown loader.
        if (!string.IsNullOrEmpty(inheritsFrom))
            return ModLoader.Other;

        return ModLoader.None;
    }

    private static bool StartsWithCI(string value, string prefix)
        => !string.IsNullOrEmpty(value) && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsCI(string value, string substring)
        => !string.IsNullOrEmpty(value) && value.Contains(substring, StringComparison.OrdinalIgnoreCase);
}
