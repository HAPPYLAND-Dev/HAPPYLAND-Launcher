namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;

/// <summary>
/// Minecraft skin arm-width variant. Maps 1-to-1 onto the Mojang skins-upload API
/// query parameter: <c>classic</c> (Steve, 4-pixel-wide arms) or <c>slim</c>
/// (Alex, 3-pixel-wide arms).
/// </summary>
public enum SkinVariant
{
    /// <summary>Classic 4-pixel arms ("Steve" model).</summary>
    Classic = 0,

    /// <summary>Slim 3-pixel arms ("Alex" model).</summary>
    Slim = 1,
}
