namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Presence;

/// <summary>
/// Rich-presence sink (e.g. Discord). The launcher publishes "idle" on startup and
/// "playing &lt;version&gt; - &lt;instance&gt;" once a Minecraft process is up; on shutdown
/// it disconnects so the user's Discord status reverts.
/// </summary>
/// <remarks>
/// The Core abstraction is dependency-free so the test project can target it without
/// pulling Discord or Avalonia. The App project ships <c>DiscordPresenceService</c>;
/// when Discord is disabled or unavailable, <see cref="NullPresenceService"/> stands in
/// so callers never have to null-check.
/// </remarks>
public interface IPresenceService
{
    /// <summary>Publish the idle state: "In Hyperion launcher".</summary>
    void SetIdle();

    /// <summary>
    /// Publish the playing state. Format: "Playing &lt;versionId&gt; - &lt;instanceName&gt;"
    /// (instance segment dropped when <paramref name="instanceName"/> is null/empty).
    /// </summary>
    void SetPlaying(string versionId, string? instanceName);

    /// <summary>Disconnect from the presence backend (Discord IPC, etc.).</summary>
    void Stop();
}
