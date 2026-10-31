using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing.Packs;

/// <summary>Pack file name to the name the game shows (no <c>.zip</c> / <c>.zip.disabled</c>).</summary>
public static class PackNames
{
    public static string Display(string filename)
    {
        if (filename.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase))
            return filename[..^".zip.disabled".Length];
        if (filename.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return filename[..^".zip".Length];
        return filename;
    }
}
