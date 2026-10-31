namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Settings;

/// <summary>
/// Persisted Hyperion launcher preferences. Lives at
/// <c>%LOCALAPPDATA%\HyperionMinecraftLauncher\settings.json</c> on Windows
/// (next to the logs directory). Defaults are tuned for a 16 GB Windows desktop;
/// the slider in the Settings page clamps to a sane fraction of the detected RAM.
/// </summary>
public sealed record LauncherSettings
{
    /// <summary>JVM minimum heap in MiB. Default 1024 (1 GB).</summary>
    public int MinimumRamMb { get; init; } = 1024;

    /// <summary>JVM maximum heap in MiB. Default 4096 (4 GB).</summary>
    public int MaximumRamMb { get; init; } = 4096;

    /// <summary>Extra JVM args appended after the heap flags. Default is empty so CmlLib's own args apply.</summary>
    public string JvmArguments { get; init; } = string.Empty;

    /// <summary>Override the <c>.minecraft</c> root. <c>null</c> = OS default.</summary>
    public string? GameDirectory { get; init; }

    /// <summary>Override the Java executable. <c>null</c> = CmlLib's auto-detect / bundled runtime.</summary>
    public string? JavaExecutable { get; init; }

    /// <summary>If true, the launcher window stays open after the Minecraft process starts.</summary>
    public bool KeepLauncherOpen { get; init; } = true;

    /// <summary>If true, the in-app log mirrors the game's stdout/stderr (planned hook - not wired yet).</summary>
    public bool ShowGameLog { get; init; }

    /// <summary>If true, the left sidebar starts collapsed (icons only, no labels). Default false (expanded).</summary>
    public bool SidebarCollapsed { get; init; }

    /// <summary>
    /// If true, the launcher publishes Discord Rich Presence ("In Hyperion launcher" / "Playing &lt;version&gt;").
    /// Default <c>true</c>; falls back to a no-op when Discord isn't running.
    /// </summary>
    public bool DiscordRpcEnabled { get; init; } = true;

    /// <summary>
    /// CurseForge API key. Default is empty - CurseForge requires per-developer keys and we
    /// don't ship one. When empty the launcher silently disables CurseForge search and any
    /// attempt to download a CurseForge file throws a "key not configured" error. Users who
    /// register their own free key at <c>https://console.curseforge.com</c> can paste it into
    /// Settings to light up the integration.
    /// </summary>
    public string CurseForgeApiKey { get; init; } = string.Empty;

    /// <summary>
    /// If true, the launcher polls the GitHub Releases endpoint on startup and surfaces a
    /// banner when a newer version is available. The check is best-effort and never blocks
    /// startup; the user clicks through to the release page in the default browser (no
    /// auto-install). Default <c>true</c>.
    /// </summary>
    public bool AutoUpdateCheckEnabled { get; init; } = true;

    /// <summary>
    /// If true, the launcher zips every world under each instance's <c>saves/</c> into
    /// <c>backups/</c> before invoking the underlying launch. Default <c>true</c> -
    /// Prism's safety net for the "MultiMC ate my world" class of incident. A failure
    /// during the backup is logged as a warning but never blocks the launch.
    /// </summary>
    public bool AutoBackupBeforeLaunch { get; init; } = true;

    /// <summary>
    /// How many backups to retain per world. After each pre-launch backup, the launcher
    /// prunes older zips per world down to this count. Default <c>5</c>; values &lt;= 0
    /// disable pruning (keep everything).
    /// </summary>
    public int AutoBackupKeepLatest { get; init; } = 5;

    /// <summary>
    /// UI culture override for the launcher (BCP 47 tag, e.g. <c>en</c>, <c>ru</c>, <c>fr-CA</c>).
    /// When <c>null</c> the launcher falls back to <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>
    /// (the OS-configured display language). Defaults to <c>null</c> = "use system default".
    /// </summary>
    public string? Locale { get; init; }

    /// <summary>
    /// Developer / QA toggle: force the launcher into offline-session mode for every launch,
    /// regardless of whether a Microsoft session is cached. When <c>true</c>:
    /// <list type="bullet">
    ///   <item><description>The Home page's Username textbox stays editable even when a Microsoft account is signed in.</description></item>
    ///   <item><description><c>LaunchCoreAsync</c> bypasses the cached Microsoft session and runs <c>AuthMode.Offline</c>
    ///     with the Username from the textbox, producing a CmlLib offline-session (accessToken=0, UUID derived from the name).</description></item>
    ///   <item><description>A small "TEST MODE" badge appears next to the Username row so it's obvious the next Play
    ///     will not produce a real-account session.</description></item>
    /// </list>
    /// Intended use: smoke-testing the download / install / launch pipeline without burning a Microsoft sign-in,
    /// or repro'ing offline-only bugs. Default <c>false</c>; the Microsoft cache is never touched by this flag.
    /// </summary>
    public bool ForceOfflineMode { get; init; }
}
