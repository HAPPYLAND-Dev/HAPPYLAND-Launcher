using System;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;

/// <summary>
/// Coordinates the full headless-server lifecycle for a registered <see cref="HeadlessServer"/>:
/// ensure the jar is downloaded + sha1 verified, ensure a matching Java runtime is on disk,
/// spawn the process via <see cref="IHeadlessServerProcess"/>, stream stdout lines, and shut
/// the JVM down gracefully on <see cref="StopAsync"/>.
/// </summary>
/// <remarks>
/// One instance, many servers. The orchestrator tracks per-server state internally so a
/// single MainViewModel can subscribe once and drive every status label off the same stream.
/// </remarks>
public interface IHeadlessServerOrchestrator
{
    /// <summary>
    /// Begin starting <paramref name="server"/>. Transitions the tracked state from
    /// <see cref="HeadlessServerState.Stopped"/> to <see cref="HeadlessServerState.Starting"/>,
    /// then to <see cref="HeadlessServerState.Running"/> once the JVM is alive.
    /// </summary>
    Task StartAsync(HeadlessServer server, CancellationToken cancellationToken);

    /// <summary>
    /// Send <c>stop</c> over stdin and wait for the JVM to exit (forces kill after a grace
    /// period). Transitions the tracked state through
    /// <see cref="HeadlessServerState.Stopping"/> back to <see cref="HeadlessServerState.Stopped"/>.
    /// </summary>
    Task StopAsync(string serverId, CancellationToken cancellationToken);

    /// <summary>
    /// Write <paramref name="command"/> to a running server's stdin. Throws when the server
    /// isn't currently in <see cref="HeadlessServerState.Running"/>.
    /// </summary>
    Task SendCommandAsync(string serverId, string command, CancellationToken cancellationToken);

    /// <summary>Current state for <paramref name="serverId"/>. Returns
    /// <see cref="HeadlessServerState.Stopped"/> for unknown ids.</summary>
    HeadlessServerState GetState(string serverId);

    /// <summary>Stdout line stream for a running server, or <c>null</c> when no JVM is alive
    /// for <paramref name="serverId"/>. Survives state transitions; completes when the process exits.</summary>
    IObservable<string>? GetStdoutLines(string serverId);

    /// <summary>OS pid for a running server, or <c>0</c> when the server is not running.</summary>
    int GetProcessId(string serverId);

    /// <summary>Fires every time a tracked server transitions between states.</summary>
    event EventHandler<HeadlessServerStateChange>? StateChanged;
}
