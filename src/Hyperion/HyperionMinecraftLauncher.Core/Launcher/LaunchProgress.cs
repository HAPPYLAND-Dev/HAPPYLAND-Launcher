namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

/// <summary>
/// Progress callback payload for the install + launch pipeline.
/// </summary>
public sealed record LaunchProgress
{
    /// <summary>Free-form stage name (e.g. <c>"Downloading library"</c>, <c>"Starting game"</c>).</summary>
    public required string Stage { get; init; }

    /// <summary>0..1 fractional progress. <c>null</c> when indeterminate.</summary>
    public double? Fraction { get; init; }

    /// <summary>Currently-processed file or item name, if applicable.</summary>
    public string? CurrentItem { get; init; }
}
