using System;
using System.IO;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Playtime;

/// <summary>
/// Dates the end of a session whose game exited while the launcher was closed. Minecraft
/// appends to <c>logs/latest.log</c> until shutdown, so its last write time is a good proxy
/// for the exit moment.
/// </summary>
public static class PlaySessionRecovery
{
    /// <summary>Last write time of <c>&lt;gameDir&gt;/logs/latest.log</c>, or null when there is none.</summary>
    public static DateTimeOffset? LatestLogWrite(string? gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory)) return null;
        var log = Path.Combine(gameDirectory, "logs", "latest.log");
        return File.Exists(log) ? new DateTimeOffset(File.GetLastWriteTimeUtc(log), TimeSpan.Zero) : null;
    }

    /// <summary>
    /// The session's end if the log write falls inside it (after the start, not in the future),
    /// otherwise null: an older log belongs to a previous session, a future one means clock skew.
    /// </summary>
    public static DateTimeOffset? EstimateEnd(PlaySession session, DateTimeOffset? lastLogWriteUtc, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        return lastLogWriteUtc is { } t && t > session.StartedAt && t <= now ? t : null;
    }
}
