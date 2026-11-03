using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.VersionMetadata;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Util;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Versions;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

// Extra JVM args are whitespace-separated. We split with simple Split-on-whitespace
// semantics because MArgument expects one element per JVM token. A more elaborate
// shell-style tokenizer is out of scope - users adding quoted args with embedded
// spaces are an edge case the global Settings page has never supported either.

/// <summary>
/// Production implementation of <see cref="IUnderlyingLauncher"/> that delegates to
/// <see cref="MinecraftLauncher"/>. All translation between CmlLib's types and our
/// neutral DTOs happens here; the public service sits on top of the interface and
/// stays free of CmlLib-specific types.
/// </summary>
public sealed class CmlLibUnderlyingLauncher : IUnderlyingLauncher
{
    private readonly MinecraftLauncher _launcher;

    /// <summary>Construct an underlying launcher with a default <c>.minecraft</c> directory.</summary>
    public CmlLibUnderlyingLauncher()
        : this(new MinecraftLauncher())
    {
    }

    /// <summary>Construct with a specific game directory.</summary>
    public CmlLibUnderlyingLauncher(string gameDirectory)
        : this(new MinecraftLauncher(gameDirectory))
    {
    }

    /// <summary>Construct with a pre-built <see cref="MinecraftLauncher"/> (useful for advanced wiring).</summary>
    public CmlLibUnderlyingLauncher(MinecraftLauncher launcher)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VersionMetadata>> GetAllVersionsAsync(CancellationToken cancellationToken)
    {
        var versions = await _launcher.GetAllVersionsAsync(cancellationToken).ConfigureAwait(false);

        var result = new List<VersionMetadata>(capacity: 1024);
        foreach (var v in versions)
        {
            result.Add(new VersionMetadata
            {
                Name = v.Name ?? string.Empty,
                Type = v.Type ?? string.Empty,
                ReleaseTime = v.ReleaseTime,
            });
        }

        // T18: We rely on CmlLib's manifest order (newest-first, the same order Mojang ships
        // the JSON). Removing the previous defensive re-sort saves ~3 ms per refresh on a cold
        // start and one list-wide quicksort allocation per ~1k entries. If CmlLib ever changes
        // the order we'll see it in the version-picker UI immediately and can re-add a sort here.
        return result;
    }

    /// <inheritdoc />
    public async Task InstallAsync(
        string versionName,
        IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(versionName))
            throw new ArgumentException("versionName is required", nameof(versionName));

        IProgress<InstallerProgressChangedEventArgs>? fileProgress = null;
        IProgress<ByteProgress>? byteProgress = null;

        if (progress is not null)
        {
            var progressGate = new object();
            var byteProgressStarted = false;
            fileProgress = new SyncProgress<InstallerProgressChangedEventArgs>(args =>
            {
                lock (progressGate)
                {
                    if (byteProgressStarted) return;
                    progress.Report(new LaunchProgress
                    {
                        Stage = "Preparing game",
                        CurrentItem = args.Name,
                    });
                }
            });

            byteProgress = new SyncProgress<ByteProgress>(args =>
            {
                lock (progressGate)
                {
                    byteProgressStarted = true;
                    progress.Report(new LaunchProgress
                    {
                        Stage = "Downloading bytes",
                        Fraction = args.TotalBytes > 0 ? (double)args.ProgressedBytes / args.TotalBytes : null,
                    });
                }
            });
        }

        await _launcher.InstallAsync(versionName, fileProgress, byteProgress, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StartProcessResult> StartProcessAsync(
        LaunchRequest request,
        bool captureGameLog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Session);

        if (string.IsNullOrWhiteSpace(request.VersionName))
            throw new ArgumentException("VersionName is required", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Session.Username))
            throw new ArgumentException("Session.Username is required", nameof(request));

        var session = new MSession
        {
            Username = request.Session.Username,
            UUID = request.Session.Uuid,
            AccessToken = request.Session.AccessToken,
        };

        var options = new MLaunchOption
        {
            Session = session,
        };

        if (request.MinimumRamMb is int min) options.MinimumRamMb = min;
        if (request.MaximumRamMb is int max) options.MaximumRamMb = max;

        // Per-instance overrides for screen dimensions. CmlLib emits -width/-height
        // game args from these so Minecraft starts in the requested window size.
        if (request.ScreenWidth is int w && w > 0) options.ScreenWidth = w;
        if (request.ScreenHeight is int h && h > 0) options.ScreenHeight = h;
        options.FullScreen = request.FullScreen;

        // Per-instance game-directory override. The chosen path becomes both the version
        // root (so jars/libraries resolve there) and the working dir Minecraft writes to.
        if (!string.IsNullOrWhiteSpace(request.GameDirectory)) options.Path = new MinecraftPath(request.GameDirectory);

        // Per-instance extra JVM args. Whitespace-split; CmlLib joins them after its built-in
        // heap flags so user args land near the end of the command line.
        if (!string.IsNullOrWhiteSpace(request.JvmArguments))
        {
            var tokens = request.JvmArguments
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0)
            {
                var jvm = new List<MArgument>(tokens.Length);
                foreach (var t in tokens) jvm.Add(new MArgument(t));
                options.ExtraJvmArguments = jvm;
            }
        }

        if (request.ExtraJvmArguments.Count > 0)
        {
            var arguments = options.ExtraJvmArguments?.ToList() ?? new List<MArgument>();
            arguments.AddRange(request.ExtraJvmArguments.Select(argument => new MArgument(argument)));
            options.ExtraJvmArguments = arguments;
        }

        // Auto-downloaded Adoptium Temurin: when the service has pre-resolved a JRE for the
        // requested Java family (1.20.5+ -> Java 21, etc.), thread the absolute path through to
        // CmlLib so its JVM auto-detect is bypassed entirely.
        if (!string.IsNullOrWhiteSpace(request.JavaPath)) options.JavaPath = request.JavaPath;

        // Quick Play: append the corresponding Minecraft 1.20+ game arguments. CmlLib 4.0.6
        // exposes QuickPlaySingleplayer/Realms/Path on MLaunchOption but no Multiplayer property
        // (the legacy --server/--port pair lives on ServerIp/ServerPort and Minecraft 1.20+ prefers
        // the unified --quickPlayMultiplayer host:port flag). We project both branches into
        // ExtraGameArguments so the args are appended literally regardless of which feature
        // gates the version manifest declares.
        var legacy = Version.TryParse(request.VersionName.Split('-')[0], out var gameVersion) && gameVersion < new Version(1, 20);
        if (legacy && request.QuickPlay is QuickPlay.Multiplayer server)
        {
            options.ServerIp = server.Host;
            options.ServerPort = server.Port;
        }
        var extraArgs = legacy && request.QuickPlay is QuickPlay.Multiplayer ? Array.Empty<string>() : BuildQuickPlayArgs(request.QuickPlay);
        if (extraArgs.Count > 0)
        {
            var marshalled = new List<MArgument>(extraArgs.Count);
            foreach (var s in extraArgs) marshalled.Add(new MArgument(s));
            options.ExtraGameArguments = marshalled;
        }

        var process = await _launcher.BuildProcessAsync(request.VersionName, options, cancellationToken).ConfigureAwait(false);

        foreach (var (name, value) in request.EnvironmentVariables)
        {
            if (value is null) process.StartInfo.Environment.Remove(name);
            else process.StartInfo.Environment[name] = value;
        }

        // Game-log capture (T-stdout-pipe, v0.32.1): only wire stdout/stderr redirection when the
        // caller asked for it. Production launches with ShowGameLog=false keep the default
        // shell-attached pipes (no per-line cost, child writes straight to the parent terminal
        // / GUI subsystem). When capture is on we flip RedirectStandardOutput/Error before Start()
        // and drain the pipes into a multicast Subject<GameLogLine> through the
        // OutputDataReceived/ErrorDataReceived events. Process.Exited completes the observable
        // so subscribers can dispose deterministically. EnableRaisingEvents is required for the
        // Exited event to fire when the process actually quits.
        IObservable<GameLogLine>? logStream = null;
        if (captureGameLog)
        {
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;

            var subject = new Subject<GameLogLine>();
            logStream = subject;
            if (request.GameLogObserver is { } observer) subject.Subscribe(observer);

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is string line)
                    subject.OnNext(new GameLogLine(line, GameLogStream.Stdout, DateTimeOffset.UtcNow));
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is string line)
                    subject.OnNext(new GameLogLine(line, GameLogStream.Stderr, DateTimeOffset.UtcNow));
            };

            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => subject.OnCompleted();

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        else
        {
            process.Start();
        }

        return new StartProcessResult { ProcessId = process.Id, GameLogStream = logStream };
    }

    /// <summary>
    /// Translate a <see cref="QuickPlay"/> target into the matching Minecraft 1.20+ game-arg pair.
    /// Returns an empty list for <see cref="QuickPlay.None"/>.
    /// </summary>
    /// <remarks>
    /// Public for unit testing the arg-emission rules without needing a real CmlLib process build.
    /// </remarks>
    public static IReadOnlyList<string> BuildQuickPlayArgs(QuickPlay quickPlay) => quickPlay switch
    {
        QuickPlay.Multiplayer mp => new[] { "--quickPlayMultiplayer", $"{FormatServerHost(mp.Host)}:{mp.Port}" },
        QuickPlay.Singleplayer sp => new[] { "--quickPlaySingleplayer", sp.WorldFolderName },
        _ => Array.Empty<string>(),
    };

    private static string FormatServerHost(string host) =>
        !host.StartsWith('[') && System.Net.IPAddress.TryParse(host, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{host}]" : host;

}
