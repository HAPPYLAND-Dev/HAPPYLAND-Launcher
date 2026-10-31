using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

/// <summary>
/// Which of the two child-process pipes a <see cref="GameLogLine"/> came from.
/// </summary>
public enum GameLogStream
{
    /// <summary>The Minecraft Java process' standard output (<c>System.out</c>).</summary>
    Stdout,

    /// <summary>The Minecraft Java process' standard error (<c>System.err</c>).</summary>
    Stderr,
}

/// <summary>
/// One line of output captured from the running Minecraft Java process. Emitted by
/// <see cref="LaunchResult.GameLogStream"/> whenever
/// <see cref="Settings.LauncherSettings.ShowGameLog"/> is on.
/// </summary>
/// <param name="Text">The raw line as written by the game, without the trailing newline.</param>
/// <param name="Stream">Which pipe the line came from (<see cref="GameLogStream.Stdout"/> or <see cref="GameLogStream.Stderr"/>).</param>
/// <param name="At">UTC timestamp at which the launcher observed the line.</param>
public sealed record GameLogLine(string Text, GameLogStream Stream, DateTimeOffset At);
