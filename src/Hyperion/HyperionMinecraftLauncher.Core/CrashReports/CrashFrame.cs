namespace TechTeaStudio.HyperionMinecraftLauncher.Core.CrashReports;

/// <summary>
/// One stack-trace line that the parser flagged as belonging to a third-party mod, plus
/// the metadata the UI needs to offer a "search for this mod" jump. The frame text is
/// preserved verbatim so the viewer can show the user exactly which line was suspect.
/// </summary>
public sealed record CrashFrame
{
    /// <summary>The raw stack-trace line, e.g. <c>"at sodium.client.Foo.bar(Foo.java:42)"</c>.</summary>
    public required string FrameLine { get; init; }

    /// <summary>The mod id we inferred from the frame's package prefix (e.g. <c>"sodium"</c>). Null when we could not extract one.</summary>
    public string? ModId { get; init; }

    /// <summary>Friendlier display label - matches the <c>mods loaded</c> block when available, else equals <see cref="ModId"/>.</summary>
    public string? ModDisplayName { get; init; }

    /// <summary>Pre-built Modrinth search URL for <see cref="ModId"/>. Null when no mod id was inferred.</summary>
    public string? ModrinthSearchUrl { get; init; }

    /// <summary>Pre-built CurseForge search URL for <see cref="ModId"/>. Null when no mod id was inferred.</summary>
    public string? CurseForgeSearchUrl { get; init; }
}
