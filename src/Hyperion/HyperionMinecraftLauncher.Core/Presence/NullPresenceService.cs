namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Presence;

/// <summary>
/// No-op <see cref="IPresenceService"/>. Wired up when the user has disabled Discord
/// Rich Presence (or when the real backend failed to initialize) so the rest of the
/// app can call presence methods unconditionally.
/// </summary>
public sealed class NullPresenceService : IPresenceService
{
    /// <inheritdoc />
    public void SetIdle()
    {
        // intentionally empty
    }

    /// <inheritdoc />
    public void SetPlaying(string versionId, string? instanceName)
    {
        // intentionally empty - tolerates null instanceName by design
    }

    /// <inheritdoc />
    public void Stop()
    {
        // intentionally empty
    }
}
