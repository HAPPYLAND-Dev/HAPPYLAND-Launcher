using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

/// <summary>
/// What <see cref="IUnderlyingLauncher.StartProcessAsync"/> hands back to the public
/// <see cref="CmlLibMinecraftLauncherService"/>: the spawned process id plus, optionally,
/// a multicast observable that emits the game's stdout/stderr lines for the lifetime of
/// the process.
/// </summary>
/// <remarks>
/// The observable is <c>null</c> when the caller asked not to capture game logs (the
/// <c>captureGameLog</c> flag on <see cref="IUnderlyingLauncher.StartProcessAsync"/> was
/// <c>false</c>) and when the underlying launcher cannot redirect the pipes for some reason.
/// </remarks>
public sealed record StartProcessResult
{
    /// <summary>OS process id of the started Java process.</summary>
    public required int ProcessId { get; init; }

    /// <summary>
    /// Live stream of <see cref="GameLogLine"/> emissions. The launcher fires
    /// <see cref="IObserver{T}.OnCompleted"/> once the process exits. <c>null</c> when
    /// game-log capture was disabled or not supported.
    /// </summary>
    public IObservable<GameLogLine>? GameLogStream { get; init; }
}
