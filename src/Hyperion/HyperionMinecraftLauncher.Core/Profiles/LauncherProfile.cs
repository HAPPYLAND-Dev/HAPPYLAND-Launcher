using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Profiles;

/// <summary>
/// One entry from <c>launcher_profiles.json</c> as written by Mojang's modern launcher.
/// We surface every well-known field; unknown fields are ignored on read and won't be
/// written back (the launcher's auth blobs, telemetry tokens, and so on stay in their file).
/// </summary>
public sealed record LauncherProfile
{
    /// <summary>Map key in the on-disk <c>profiles</c> object (usually a 32-hex GUID, or a sentinel like <c>"latest-release"</c>).</summary>
    public required string Key { get; init; }

    /// <summary>Display name. May be empty for the built-in "Latest release" / "Latest snapshot" rows.</summary>
    public required string Name { get; init; }

    /// <summary>One of <c>"custom"</c>, <c>"latest-release"</c>, <c>"latest-snapshot"</c>.</summary>
    public required string Type { get; init; }

    /// <summary>ISO-8601 creation timestamp if present.</summary>
    public DateTimeOffset? Created { get; init; }

    /// <summary>ISO-8601 last-used timestamp if present.</summary>
    public DateTimeOffset? LastUsed { get; init; }

    /// <summary>The version id this profile launches. Matches <c>versions/&lt;id&gt;/&lt;id&gt;.json</c>.</summary>
    public string? LastVersionId { get; init; }

    /// <summary>Icon hint: preset name like <c>"Furnace"</c> / <c>"Grass"</c>, or a <c>data:image/png;base64,...</c> URI.</summary>
    public string? Icon { get; init; }

    /// <summary>Override game data directory. Default = <c>.minecraft</c>.</summary>
    public string? GameDir { get; init; }

    /// <summary>Full path to a specific Java executable.</summary>
    public string? JavaDir { get; init; }

    /// <summary>Custom JVM args (e.g. <c>"-Xmx4G -XX:+UseG1GC"</c>).</summary>
    public string? JavaArgs { get; init; }

    /// <summary>Preferred window width.</summary>
    public int? ResolutionWidth { get; init; }

    /// <summary>Preferred window height.</summary>
    public int? ResolutionHeight { get; init; }

    /// <summary>One-line label for ComboBox / ListBox display.</summary>
    public string DisplayText => string.IsNullOrEmpty(Type) ? Name : $"{Name}  -  {Type}";
}
