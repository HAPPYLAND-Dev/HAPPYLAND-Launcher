namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

/// <summary>
/// The three JRE feature versions Minecraft has required across its history.
/// Mojang bumps the minimum JRE every few major versions and the launcher follows.
/// </summary>
public enum JavaRequirement
{
    /// <summary>Required by Minecraft 1.16.x and earlier (1.8 .. 1.16.5).</summary>
    Java8,

    /// <summary>Required by Minecraft 1.17 through 1.20.4.</summary>
    Java17,

    /// <summary>Required by Minecraft 1.20.5 and later.</summary>
    Java21,

    /// <summary>Required by Minecraft releases using Java 25.</summary>
    Java25,
}
