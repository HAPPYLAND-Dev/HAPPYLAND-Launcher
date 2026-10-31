namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;

/// <summary>The mod loader (if any) responsible for an installed Minecraft version.</summary>
public enum ModLoader
{
    /// <summary>Plain Mojang vanilla.</summary>
    None,

    /// <summary>Forge 1.13+ (<c>cpw.mods.modlauncher.Launcher</c>).</summary>
    Forge,

    /// <summary>NeoForge fork (Forge family, 1.17+ <c>BootstrapLauncher</c>).</summary>
    NeoForge,

    /// <summary>Fabric (<c>net.fabricmc.loader.impl.launch.knot.KnotClient</c>).</summary>
    Fabric,

    /// <summary>Quilt (<c>org.quiltmc.loader.impl.launch.knot.KnotClient</c>).</summary>
    Quilt,

    /// <summary>OptiFine standalone (rare; usually shipped on top of Forge).</summary>
    OptiFine,

    /// <summary>Legacy Forge (1.7.10 era, <c>cpw.mods.fml.*</c>).</summary>
    LegacyForge,

    /// <summary>Unknown loader (the manifest has <c>inheritsFrom</c> but no recognised <c>mainClass</c>).</summary>
    Other,
}
