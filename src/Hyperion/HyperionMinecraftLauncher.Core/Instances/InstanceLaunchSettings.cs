using TechTeaStudio.HyperionMinecraftLauncher.Core.Settings;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

/// <summary>
/// The resolved per-launch settings after merging a single <see cref="Instance"/>'s
/// optional overrides over the global <see cref="LauncherSettings"/>. Pure value type
/// with no mutation - the launch flow builds one of these via
/// <see cref="Merge"/> right before constructing the <c>LaunchRequest</c>.
/// </summary>
/// <remarks>
/// Merge order: instance override wins when set, else the global default.
/// "Set" means a non-null int OR a non-blank string. Blank-string overrides are
/// treated as "no override" so a user can clear a textbox to inherit again.
/// </remarks>
public readonly record struct InstanceLaunchSettings
{
    /// <summary>JVM minimum heap (MiB).</summary>
    public int MinimumRamMb { get; init; }

    /// <summary>JVM maximum heap (MiB).</summary>
    public int MaximumRamMb { get; init; }

    /// <summary>Extra JVM args appended after heap flags. Empty string when neither side set anything.</summary>
    public string JvmArguments { get; init; }

    /// <summary>Game-directory override. <c>null</c> = OS default <c>.minecraft</c>.</summary>
    public string? GameDirectory { get; init; }

    /// <summary>Window width override. <c>null</c> = let Minecraft decide.</summary>
    public int? ResolutionWidth { get; init; }

    /// <summary>Window height override. <c>null</c> = let Minecraft decide.</summary>
    public int? ResolutionHeight { get; init; }

    /// <summary>
    /// Resolve the launch-time values for <paramref name="instance"/> by overlaying its
    /// non-null / non-blank overrides on top of the global <paramref name="settings"/>.
    /// </summary>
    /// <param name="instance">Instance whose overrides take precedence. May be <c>null</c> -
    /// in which case the returned struct equals the global settings verbatim.</param>
    /// <param name="settings">Global launcher settings. Required.</param>
    public static InstanceLaunchSettings Merge(Instance? instance, LauncherSettings settings)
    {
        System.ArgumentNullException.ThrowIfNull(settings);

        if (instance is null)
        {
            return new InstanceLaunchSettings
            {
                MinimumRamMb = settings.MinimumRamMb,
                MaximumRamMb = settings.MaximumRamMb,
                JvmArguments = settings.JvmArguments ?? string.Empty,
                GameDirectory = NormalizeDir(settings.GameDirectory),
                ResolutionWidth = null,
                ResolutionHeight = null,
            };
        }

        // A null or zero MinimumRamMb / MaximumRamMb means "inherit". We treat 0
        // the same as null so a partially-cleared dialog doesn't accidentally cap
        // memory at zero.
        var min = (instance.MinimumRamMb is int m && m > 0) ? m : settings.MinimumRamMb;
        var max = (instance.MaximumRamMb is int M && M > 0) ? M : settings.MaximumRamMb;

        // Defensive: if the instance only overrode min but settings.max is lower, raise max
        // to match so the JVM doesn't reject Xmx < Xms.
        if (max < min) max = min;

        var jvm = instance.JvmArguments;
        var resolvedJvm = string.IsNullOrWhiteSpace(jvm)
            ? (settings.JvmArguments ?? string.Empty)
            : jvm;

        var dir = NormalizeDir(instance.GameDirectory) ?? NormalizeDir(settings.GameDirectory);

        var width = (instance.ResolutionWidth is int w && w > 0) ? w : (int?)null;
        var height = (instance.ResolutionHeight is int h && h > 0) ? h : (int?)null;

        return new InstanceLaunchSettings
        {
            MinimumRamMb = min,
            MaximumRamMb = max,
            JvmArguments = resolvedJvm,
            GameDirectory = dir,
            ResolutionWidth = width,
            ResolutionHeight = height,
        };
    }

    private static string? NormalizeDir(string? candidate)
        => string.IsNullOrWhiteSpace(candidate) ? null : candidate;
}
