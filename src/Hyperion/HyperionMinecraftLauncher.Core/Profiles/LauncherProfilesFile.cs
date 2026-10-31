using System.Collections.Generic;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Profiles;

/// <summary>Snapshot of a parsed <c>launcher_profiles.json</c>.</summary>
public sealed record LauncherProfilesFile
{
    /// <summary>The profiles, in stable map-iteration order. Empty when the file is absent.</summary>
    public required IReadOnlyList<LauncherProfile> Profiles { get; init; }

    /// <summary>Schema version from the top-level <c>version</c> key. Mojang's modern launcher writes <c>3</c>.</summary>
    public int SchemaVersion { get; init; }
}
