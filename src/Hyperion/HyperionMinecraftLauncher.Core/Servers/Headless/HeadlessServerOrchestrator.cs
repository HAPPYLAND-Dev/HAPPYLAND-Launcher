using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;

/// <summary>
/// Default <see cref="IHeadlessServerOrchestrator"/>. Wires the jar fetcher, the Java runtime
/// manager, and a process factory together to drive the per-server state machine
/// (<see cref="HeadlessServerState.Stopped"/> -> Starting -> Running -> Stopping -> Stopped).
/// Re-entrant calls for the same server short-circuit so double-clicking Start can't fork
/// two JVMs against the same world.
/// </summary>
public sealed class HeadlessServerOrchestrator : IHeadlessServerOrchestrator
{
    /// <summary>How long <see cref="StopAsync"/> waits for the JVM to exit cleanly before
    /// resorting to <see cref="System.Diagnostics.Process.Kill(bool)"/>.</summary>
    public static readonly TimeSpan DefaultGracefulShutdown = TimeSpan.FromSeconds(15);

    private readonly IHeadlessServerJarFetcher _jarFetcher;
    private readonly IJavaRuntimeManager _javaRuntimeManager;
    private readonly Func<IHeadlessServerProcess> _processFactory;
    private readonly TimeSpan _gracefulShutdown;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>Standard production constructor.</summary>
    public HeadlessServerOrchestrator(
        IHeadlessServerJarFetcher jarFetcher,
        IJavaRuntimeManager javaRuntimeManager,
        Func<IHeadlessServerProcess> processFactory,
        TimeSpan? gracefulShutdown = null)
    {
        _jarFetcher = jarFetcher ?? throw new ArgumentNullException(nameof(jarFetcher));
        _javaRuntimeManager = javaRuntimeManager ?? throw new ArgumentNullException(nameof(javaRuntimeManager));
        _processFactory = processFactory ?? throw new ArgumentNullException(nameof(processFactory));
        _gracefulShutdown = gracefulShutdown ?? DefaultGracefulShutdown;
    }

    /// <inheritdoc />
    public event EventHandler<HeadlessServerStateChange>? StateChanged;

    /// <inheritdoc />
    public async Task StartAsync(HeadlessServer server, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);

        var entry = _entries.GetOrAdd(server.Id, _ => new Entry());
        // Single-flight per server. Double-clicking Start while a start is in progress is a no-op.
        if (!entry.TransitionGate.Wait(0))
            return;

        try
        {
            if (entry.State != HeadlessServerState.Stopped)
                return;

            SetState(server.Id, entry, HeadlessServerState.Starting, processId: 0);

            // 1) Ensure the server jar is on disk + sha1-verified.
            await _jarFetcher.EnsureServerJarAsync(server.VersionId, server.Path, progress: null, cancellationToken)
                .ConfigureAwait(false);

            // 2) Ensure a Java runtime matching the MC version is on disk; resolve its absolute path.
            var requirement = JavaRequirementResolver.For(server.VersionId);
            var javaPath = await _javaRuntimeManager.EnsureRuntimeAsync(requirement, progress: null, cancellationToken)
                .ConfigureAwait(false);

            // 3) Spawn the JVM via the process factory.
            var process = _processFactory();
            try
            {
                var pid = await process.StartAsync(server, javaPath, cancellationToken).ConfigureAwait(false);
                entry.Process = process;
                // Once the JVM is alive we expose the line stream so the UI can subscribe.
                entry.StdoutLines = process.StdoutLines;
                // Reset to Stopped automatically once the JVM exits (Exited handler fires
                // through the subject's OnCompleted; we hook it via an internal observer).
                var exitObserver = new ExitObserver(this, server.Id);
                entry.ExitSubscription = process.StdoutLines.Subscribe(exitObserver);

                SetState(server.Id, entry, HeadlessServerState.Running, pid);
            }
            catch
            {
                // Spawn failure rewinds the state back to Stopped so the UI Start button re-enables.
                process.Dispose();
                SetState(server.Id, entry, HeadlessServerState.Stopped, processId: 0);
                throw;
            }
        }
        finally
        {
            entry.TransitionGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(string serverId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        if (!_entries.TryGetValue(serverId, out var entry))
            return;

        if (!entry.TransitionGate.Wait(0))
            return;

        try
        {
            if (entry.State is not (HeadlessServerState.Running or HeadlessServerState.Starting))
                return;

            var process = entry.Process;
            if (process is null)
            {
                SetState(serverId, entry, HeadlessServerState.Stopped, processId: 0);
                return;
            }

            SetState(serverId, entry, HeadlessServerState.Stopping, processId: process.ProcessId);
            try
            {
                await process.StopAsync(_gracefulShutdown, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CleanUp(entry);
                SetState(serverId, entry, HeadlessServerState.Stopped, processId: 0);
            }
        }
        finally
        {
            entry.TransitionGate.Release();
        }
    }

    /// <inheritdoc />
    public Task SendCommandAsync(string serverId, string command, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        ArgumentNullException.ThrowIfNull(command);
        if (!_entries.TryGetValue(serverId, out var entry) || entry.Process is null)
            throw new InvalidOperationException($"Headless server '{serverId}' is not running.");
        if (entry.State != HeadlessServerState.Running)
            throw new InvalidOperationException($"Headless server '{serverId}' is in state {entry.State}, cannot accept commands.");
        return entry.Process.SendCommandAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public HeadlessServerState GetState(string serverId)
    {
        if (_entries.TryGetValue(serverId, out var entry))
            return entry.State;
        return HeadlessServerState.Stopped;
    }

    /// <inheritdoc />
    public IObservable<string>? GetStdoutLines(string serverId)
    {
        if (_entries.TryGetValue(serverId, out var entry))
            return entry.StdoutLines;
        return null;
    }

    /// <inheritdoc />
    public int GetProcessId(string serverId)
    {
        if (_entries.TryGetValue(serverId, out var entry) && entry.Process is { } p)
            return p.ProcessId;
        return 0;
    }

    private void SetState(string serverId, Entry entry, HeadlessServerState newState, int processId)
    {
        var old = entry.State;
        if (old == newState) return;
        entry.State = newState;
        StateChanged?.Invoke(this, new HeadlessServerStateChange(serverId, old, newState, processId));
    }

    private static void CleanUp(Entry entry)
    {
        try { entry.ExitSubscription?.Dispose(); } catch { /* best-effort */ }
        entry.ExitSubscription = null;
        try { entry.Process?.Dispose(); } catch { /* best-effort */ }
        entry.Process = null;
        entry.StdoutLines = null;
    }

    /// <summary>
    /// When the JVM exits on its own (player typed /stop in-game, JVM crashed, killed externally)
    /// the subject's OnCompleted fires. We use it as a signal to settle the state machine back
    /// to Stopped without requiring the UI to poll.
    /// </summary>
    private sealed class ExitObserver : IObserver<string>
    {
        private readonly HeadlessServerOrchestrator _parent;
        private readonly string _serverId;
        public ExitObserver(HeadlessServerOrchestrator parent, string serverId)
        {
            _parent = parent;
            _serverId = serverId;
        }
        public void OnNext(string value) { }
        public void OnError(Exception error) => OnCompleted();
        public void OnCompleted()
        {
            if (!_parent._entries.TryGetValue(_serverId, out var entry)) return;
            // Only transition if we're not already in Stopping (StopAsync handles its own cleanup).
            if (entry.State == HeadlessServerState.Stopping) return;
            // We can't await StopAsync's gate here without risking a deadlock when the subject
            // fires from the StopAsync codepath itself; so we run cleanup non-blocking.
            CleanUp(entry);
            _parent.SetState(_serverId, entry, HeadlessServerState.Stopped, processId: 0);
        }
    }

    private sealed class Entry
    {
        public HeadlessServerState State { get; set; } = HeadlessServerState.Stopped;
        public IHeadlessServerProcess? Process { get; set; }
        public IObservable<string>? StdoutLines { get; set; }
        public IDisposable? ExitSubscription { get; set; }
        // Re-entrancy gate for state transitions. SemaphoreSlim with 1 permit ~= mutex but lets
        // us TryAcquire (Wait(0)) so concurrent Start/Stop just no-ops instead of blocking.
        public SemaphoreSlim TransitionGate { get; } = new(1, 1);
    }
}
