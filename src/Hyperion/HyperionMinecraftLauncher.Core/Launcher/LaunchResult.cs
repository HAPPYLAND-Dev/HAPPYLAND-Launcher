using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

/// <summary>Outcome of <see cref="IMinecraftLauncherService.LaunchAsync"/>.</summary>
public sealed record LaunchResult
{
    /// <summary>Operating-system process id of the spawned Java process.</summary>
    public required int ProcessId { get; init; }

    /// <summary>The version id that was actually launched (matches <see cref="LaunchRequest.VersionName"/>).</summary>
    public required string VersionName { get; init; }

    /// <summary>
    /// Live stream of stdout/stderr lines from the Minecraft process. <c>null</c> when the launcher
    /// is configured with <see cref="Settings.LauncherSettings.ShowGameLog"/> off, or when the
    /// underlying launcher cannot capture the pipes (e.g. detached launches in headless mode).
    /// The stream calls <see cref="IObserver{T}.OnCompleted"/> once the game process exits.
    /// </summary>
    /// <remarks>
    /// Subscribe to this on the UI thread to mirror the game log into the launcher's in-window
    /// text box. The observable is multicast: multiple subscribers each get every line.
    /// </remarks>
    public IObservable<GameLogLine>? GameLogStream { get; init; }
}
