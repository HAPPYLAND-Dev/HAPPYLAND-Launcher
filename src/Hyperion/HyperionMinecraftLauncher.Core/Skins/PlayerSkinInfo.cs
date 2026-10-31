namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;

/// <summary>
/// What Mojang returns about a player's textures: the raw skin PNG bytes, whether the
/// model is slim ("Alex") rather than classic ("Steve"), and an optional cape PNG.
/// </summary>
public sealed record PlayerSkinInfo
{
    /// <summary>Skin PNG (64x64 modern, occasionally 64x32 legacy).</summary>
    public required byte[] SkinPng { get; init; }

    /// <summary>True when the skin uses the 3-pixel-wide slim arms.</summary>
    public bool IsSlim { get; init; }

    /// <summary>Cape PNG (64x32) if the user has one, else null.</summary>
    public byte[]? CapePng { get; init; }
}
