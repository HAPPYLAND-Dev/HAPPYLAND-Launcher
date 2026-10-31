using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;

/// <summary>
/// Default <see cref="IHeadlessServerProcess"/> built on <see cref="Process"/>. Spawns
/// <c>{javaExecutable} -Xmx{RamMb}m -jar server.jar nogui</c> in the server's working
/// directory, redirects stdout / stderr / stdin, and surfaces every output line through
/// <see cref="StdoutLines"/>.
/// </summary>
public sealed class ProcessHeadlessServer : IHeadlessServerProcess
{
    private readonly HeadlessLineSubject _lines = new();
    private Process? _process;
    private StreamWriter? _stdin;
    private int _pid;
    private bool _disposed;

    /// <inheritdoc />
    public IObservable<string> StdoutLines => _lines;

    /// <inheritdoc />
    public bool IsRunning => _process is { } p && !p.HasExited;

    /// <inheritdoc />
    public int ProcessId => _pid;

    /// <inheritdoc />
    public Task<int> StartAsync(HeadlessServer server, string javaExecutable, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(javaExecutable);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process is not null) throw new InvalidOperationException("Process already started.");

        // We assume the orchestrator already wrote {server.Path}/server.jar via the fetcher.
        // Failing fast here surfaces the missing jar before forking Java.
        var jarPath = Path.Combine(server.Path, "server.jar");
        if (!File.Exists(jarPath))
            throw new FileNotFoundException($"server.jar not found in {server.Path}.", jarPath);

        var ramMb = server.RamMb > 0 ? server.RamMb : 2048;
        var psi = new ProcessStartInfo
        {
            FileName = javaExecutable,
            WorkingDirectory = server.Path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        psi.ArgumentList.Add($"-Xmx{ramMb}M");
        psi.ArgumentList.Add($"-Xms{Math.Max(512, Math.Min(ramMb, 1024))}M");
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add("server.jar");
        psi.ArgumentList.Add("nogui");

        var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true,
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) _lines.OnNext(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            // The vanilla dedicated server prints everything to stdout, but mod-loaded servers
            // sometimes write to stderr; merge both into the same line stream so the UI sees the lot.
            if (e.Data is not null) _lines.OnNext(e.Data);
        };
        process.Exited += (_, _) => _lines.OnCompleted();

        if (!process.Start())
            throw new InvalidOperationException("Failed to start dedicated-server process.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _process = process;
        _stdin = process.StandardInput;
        _pid = process.Id;
        return Task.FromResult(_pid);
    }

    /// <inheritdoc />
    public async Task SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var stdin = _stdin;
        if (stdin is null || _process is null || _process.HasExited)
            throw new InvalidOperationException("Process is not running.");
        // The vanilla dedicated server expects one command per newline-terminated line.
        await stdin.WriteLineAsync(command.AsMemory(), cancellationToken).ConfigureAwait(false);
        await stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(TimeSpan graceful, CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is null) return;
        if (process.HasExited) return;

        // 1) Send the polite "stop" line over stdin. Some forge / fabric servers also accept
        //    "/stop" but vanilla only honours the unprefixed form, so we use that.
        try
        {
            var stdin = _stdin;
            if (stdin is not null)
            {
                await stdin.WriteLineAsync("stop".AsMemory(), cancellationToken).ConfigureAwait(false);
                await stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // If stdin is closed (e.g. JVM crashed during shutdown) fall straight through to kill.
        }

        // 2) Wait up to `graceful` for a clean exit. Process.WaitForExitAsync respects the token
        //    but not a timeout, so we wrap it in a linked CTS.
        if (graceful > TimeSpan.Zero)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(graceful);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // graceful elapsed - fall through to Kill.
            }
        }

        // 3) Force kill if the server is still alive. entireProcessTree:true so any helper
        //    children (some launchers chain through cmd /bin/sh) don't dangle.
        if (!process.HasExited)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
            catch { /* swallow secondary cancellation */ }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _stdin?.Dispose(); } catch { /* ignore */ }
        try { _process?.Dispose(); } catch { /* ignore */ }
        // Force-complete the subject in case OnCompleted never fired (Exited handler missed).
        _lines.OnCompleted();
    }
}
