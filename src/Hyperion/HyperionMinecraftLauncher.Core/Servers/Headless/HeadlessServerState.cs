namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;

/// <summary>
/// Lifecycle state for one registered <see cref="HeadlessServer"/>. The UI binds to this
/// enum (via a converter) to label the per-server status row in the Headless Servers page.
/// </summary>
public enum HeadlessServerState
{
    /// <summary>No JVM running; the server folder is dormant.</summary>
    Stopped,

    /// <summary>Jar / Java auto-download in progress, or the JVM has been spawned but
    /// "Done" hasn't appeared in stdout yet.</summary>
    Starting,

    /// <summary>JVM is up and accepting input.</summary>
    Running,

    /// <summary>StopAsync has been issued; we're waiting for the JVM to drain.</summary>
    Stopping,
}
