namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;

/// <summary>
/// Origin of a <see cref="Mod"/>. Drives icon / link choices in the UI and dispatches
/// remote calls to the matching <see cref="IModRepository"/>.
/// </summary>
public enum ModSource
{
    /// <summary>Modrinth project (https://modrinth.com).</summary>
    Modrinth,

    /// <summary>CurseForge mod (https://curseforge.com).</summary>
    CurseForge,

    /// <summary>Mod already present on disk under <c>&lt;instance&gt;/mods/</c>.</summary>
    Local,
}
