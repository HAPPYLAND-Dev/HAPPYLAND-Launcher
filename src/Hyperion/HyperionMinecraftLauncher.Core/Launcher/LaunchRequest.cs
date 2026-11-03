using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

/// <summary>
/// Everything the launcher needs to install + start a chosen Minecraft version.
/// </summary>
public sealed record LaunchRequest
{
    /// <summary>The version id (e.g. <c>"1.21.5"</c>).</summary>
    public required string VersionName { get; init; }

    /// <summary>Authenticated session.</summary>
    public required AuthResult Session { get; init; }

    /// <summary>
    /// Override the <c>.minecraft</c> root. <c>null</c> picks the OS default
    /// (<c>%APPDATA%\.minecraft</c> on Windows, <c>~/.minecraft</c> on Linux, etc.).
    /// </summary>
    public string? GameDirectory { get; init; }

    /// <summary>JVM minimum heap in MiB. <c>null</c> leaves it to CmlLib's default.</summary>
    public int? MinimumRamMb { get; init; }

    /// <summary>JVM maximum heap in MiB. <c>null</c> leaves it to CmlLib's default.</summary>
    public int? MaximumRamMb { get; init; }

    /// <summary>
    /// Extra JVM arguments appended after the heap flags. Whitespace-separated. <c>null</c> or
    /// empty = no extras. Honored per-instance (overrides global JVM args at launch time).
    /// </summary>
    public string? JvmArguments { get; init; }

    public IReadOnlyList<string> ExtraJvmArguments { get; init; } = Array.Empty<string>();

    /// <summary>Environment overrides for the game process; null removes a variable.</summary>
    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; } = new Dictionary<string, string?>();

    /// <summary>Game window width override. <c>null</c> = let Minecraft decide.</summary>
    public int? ScreenWidth { get; init; }

    /// <summary>Game window height override. <c>null</c> = let Minecraft decide.</summary>
    public int? ScreenHeight { get; init; }
    public bool FullScreen { get; init; }

    /// <summary>
    /// Quick Play target. Defaults to <see cref="QuickPlay.None"/>, which means a regular
    /// launch into the main menu. Set to <see cref="QuickPlay.Singleplayer"/> or
    /// <see cref="QuickPlay.Multiplayer"/> to deep-link straight into a world or server.
    /// </summary>
    public QuickPlay QuickPlay { get; init; } = new QuickPlay.None();

    /// <summary>
    /// Which Java feature version this Minecraft build needs. <c>null</c> means "no managed
    /// JRE - use whatever's on PATH". The view-model populates this via
    /// <see cref="JavaRequirementResolver.For"/> when the user hasn't supplied a manual override.
    /// </summary>
    public JavaRequirement? JavaRequirement { get; init; }

    /// <summary>
    /// Pre-resolved absolute path to the <c>java(.exe)</c> the underlying launcher should use.
    /// Set by the service after <see cref="IJavaRuntimeManager.EnsureRuntimeAsync"/> returns.
    /// <c>null</c> means "let CmlLib decide".
    /// </summary>
    public string? JavaPath { get; init; }

    /// <summary>
    /// Mod loader to install on top of the vanilla <see cref="VersionName"/> before launching.
    /// Defaults to <see cref="ModLoader.None"/> (vanilla). v0.30.0 (T21a). The service runs
    /// the configured <see cref="Installations.Loaders.IModLoaderInstaller"/> ahead of the
    /// regular install + start pipeline and patches <see cref="VersionName"/> in-place
    /// with the resulting modded version id.
    /// </summary>
    public ModLoader Loader { get; init; } = ModLoader.None;

    /// <summary>
    /// Loader-specific version string (e.g. <c>"49.0.30"</c> for Forge, <c>"0.16.10"</c> for
    /// Fabric). Ignored when <see cref="Loader"/> is <see cref="ModLoader.None"/>. <c>null</c>
    /// means "let the installer pick the newest stable build".
    /// </summary>
    public string? LoaderVersion { get; init; }

    /// <summary>
    /// When <c>true</c> the underlying launcher redirects the game's stdout/stderr into a
    /// live <see cref="GameLogLine"/> observable exposed on <see cref="LaunchResult.GameLogStream"/>.
    /// Mirrors <see cref="Settings.LauncherSettings.ShowGameLog"/>. Defaults to <c>false</c> so
    /// headless callers and tests don't pay the pipe-redirection cost by accident.
    /// </summary>
    public bool CaptureGameLog { get; init; }

    /// <summary>Receives captured output from the first line, including startup messages.</summary>
    public IObserver<GameLogLine>? GameLogObserver { get; init; }
}
