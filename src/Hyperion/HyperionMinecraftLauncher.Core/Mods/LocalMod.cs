using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;

/// <summary>
/// A mod jar already present in an instance's <c>mods/</c> directory. "Disabled" mods are
/// stored as <c>.jar.disabled</c> so the loader skips them while still leaving the file
/// trivially toggleable.
/// </summary>
public sealed record LocalMod
{
    /// <summary>Bare filename (no directory). For disabled mods this ends in <c>.jar.disabled</c>.</summary>
    public required string Filename { get; init; }

    /// <summary>Pretty label - usually the filename with the extension stripped.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Size on disk in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>True when the file is <c>.jar</c>; false when <c>.jar.disabled</c>.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>SHA-1 of the file. Lazily computed by the manager and may be null.</summary>
    public string? Sha1 { get; init; }

    /// <summary>Loader inferred from the jar's metadata (if any).</summary>
    public ModLoader? DetectedLoader { get; init; }
}
