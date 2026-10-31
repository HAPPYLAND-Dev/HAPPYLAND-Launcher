using System;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;

/// <summary>
/// Lifecycle abstraction over a single dedicated-server JVM process. Implementations own the
/// underlying <see cref="System.Diagnostics.Process"/> handle plus the stdin / stdout pipes
/// so the orchestrator and the UI never touch raw process types.
/// </summary>
/// <remarks>
/// Designed for one process instance per object - i.e. construct, <see cref="StartAsync"/>,
/// use, <see cref="StopAsync"/>, dispose. Restarting requires a fresh implementation
/// instance; the orchestrator manages that lifecycle.
/// </remarks>
public interface IHeadlessServerProcess : IDisposable
{
    /// <summary>
    /// Spawn <c>java -Xmx{RamMb}m -jar server.jar nogui</c> in <paramref name="server"/>.Path
    /// with stdin / stdout redirected. Returns the new process id. Subsequent stdout lines
    /// flow through <see cref="StdoutLines"/>.
    /// </summary>
    Task<int> StartAsync(HeadlessServer server, string javaExecutable, CancellationToken cancellationToken);

    /// <summary>Write <paramref name="command"/> + newline to the server's stdin.</summary>
    Task SendCommandAsync(string command, CancellationToken cancellationToken);

    /// <summary>
    /// Hot observable that fires once per stdout line as the server writes it (interleaved
    /// stderr lines too - dedicated servers print everything to stdout, but we merge both
    /// streams for safety). Completes when the process exits.
    /// </summary>
    IObservable<string> StdoutLines { get; }

    /// <summary>
    /// Send <c>stop</c> over stdin and wait up to <paramref name="graceful"/> for the process
    /// to exit. On timeout, <see cref="System.Diagnostics.Process.Kill(bool)"/> is invoked with
    /// <c>entireProcessTree:true</c> so child processes don't dangle.
    /// </summary>
    Task StopAsync(TimeSpan graceful, CancellationToken cancellationToken);

    /// <summary><c>true</c> once <see cref="StartAsync"/> returned successfully and the
    /// process has not yet exited.</summary>
    bool IsRunning { get; }

    /// <summary>The process id assigned by the OS, or <c>0</c> when not started.</summary>
    int ProcessId { get; }
}
