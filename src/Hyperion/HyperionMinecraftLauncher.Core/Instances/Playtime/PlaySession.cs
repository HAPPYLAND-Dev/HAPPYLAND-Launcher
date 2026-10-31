using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Playtime;

/// <summary>
/// One running game process attributed to an instance. Journaled on launch so the session
/// can still be credited if the launcher is closed before the game exits.
/// </summary>
public sealed record PlaySession
{
    /// <summary><see cref="Instance.Id"/> the game was launched for.</summary>
    public required string InstanceId { get; init; }

    /// <summary>OS process id of the game (the java process).</summary>
    public required int ProcessId { get; init; }

    /// <summary>UTC moment the game process was reported as started.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Resolved game directory; its <c>logs/latest.log</c> dates an orphaned session's end.</summary>
    public string? GameDirectory { get; init; }
}
