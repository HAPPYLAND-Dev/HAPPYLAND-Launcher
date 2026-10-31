using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Cache;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.Loaders;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Java;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;
using TechTeaStudio.HyperionMinecraftLauncher.Core.News;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Profiles;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Versions;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

/// <summary>
/// CmlLib-backed implementation of <see cref="IMinecraftLauncherService"/>.
/// Owns no CmlLib types directly - all interaction is funnelled through
/// <see cref="IUnderlyingLauncher"/>, which keeps tests synchronous and offline.
/// </summary>
public sealed class CmlLibMinecraftLauncherService : IMinecraftLauncherService
{
    private readonly IUnderlyingLauncher _underlying;
    private readonly ILauncherLogger _logger;
    private readonly IMicrosoftAuthService? _microsoftAuth;
    private readonly IInstalledVersionScanner _installedScanner;
    private readonly IMinecraftInstallationLocator _installationLocator;
    private readonly ILauncherProfilesStore _profilesStore;
    private readonly IServersStore _serversStore;
    private readonly IServerPinger _serverPinger;
    private readonly INewsClient _newsClient;
    private readonly IInstanceStore _instanceStore;
    // v0.32.1: java + loader services are wrapped in Lazy<> so the App can defer their HttpClient
    // construction and folder probes to first-launch instead of paying for them on cold start.
    // The eager-instance fields stay populated by the convenience constructor + tests; lazy ones
    // win when both are set so the App's wiring takes precedence.
    private readonly IJavaRuntimeManager _javaRuntimeManager;
    private readonly IModLoaderInstaller? _modLoaderInstaller;
    private readonly Lazy<IJavaRuntimeManager>? _lazyJavaRuntimeManager;
    private readonly Lazy<IModLoaderInstaller>? _lazyModLoaderInstaller;
    private readonly FileCache? _versionManifestCache;
    private readonly TimeSpan _versionManifestTtl;

    /// <summary>Resolve the active java-runtime manager: lazy override (if any) takes precedence
    /// over the eager instance. Both paths converge on a single <see cref="IJavaRuntimeManager"/>.</summary>
    private IJavaRuntimeManager JavaRuntimeManager => _lazyJavaRuntimeManager?.Value ?? _javaRuntimeManager;

    /// <summary>Resolve the active mod-loader installer: lazy override (if any) takes precedence
    /// over the eager instance. <c>null</c> means "this build has no mod-loader support wired up".</summary>
    private IModLoaderInstaller? ModLoaderInstaller => _lazyModLoaderInstaller?.Value ?? _modLoaderInstaller;

    /// <summary>Cache key for the serialised version-manifest list under <see cref="FileCache"/>.</summary>
    private const string VersionManifestCacheKey = "versions.json";

    /// <summary>Default TTL for the version manifest disk cache. Mojang's manifest changes only
    /// when a new snapshot / release lands, so 30 minutes is plenty for picking up fresh entries
    /// while avoiding the ~1.5 s network round-trip on every cold start.</summary>
    private static readonly TimeSpan DefaultVersionManifestTtl = TimeSpan.FromMinutes(30);

    /// <summary>Primary constructor used by the App and by tests.</summary>
    /// <param name="microsoftAuth">Optional Microsoft sign-in provider. When omitted, <see cref="AuthMode.Microsoft"/>
    /// requests throw with a friendly "not configured" message - useful in headless / test contexts that don't
    /// link the WebView2-dependent <c>MicrosoftAuthService</c>.</param>
    /// <param name="installedScanner">Reads <c>versions/&lt;id&gt;/&lt;id&gt;.json</c> from disk. Defaults to a fresh
    /// <see cref="FileSystemInstalledVersionScanner"/> if omitted.</param>
    /// <param name="installationLocator">Locates the platform-default <c>.minecraft</c> directory. Defaults to
    /// <see cref="DefaultMinecraftInstallationLocator"/>.</param>
    /// <param name="javaRuntimeManager">Optional Adoptium-backed JRE downloader. Defaults to
    /// <see cref="NullJavaRuntimeManager"/> in tests and headless contexts; the App injects a real
    /// <see cref="AdoptiumJavaRuntimeManager"/>. When the request carries a non-null
    /// <see cref="LaunchRequest.JavaRequirement"/>, the service ensures the runtime is on disk
    /// before invoking the underlying CmlLib launch and patches <see cref="LaunchRequest.JavaPath"/>
    /// with the resolved <c>java(.exe)</c> path.</param>
    public CmlLibMinecraftLauncherService(
        IUnderlyingLauncher underlying,
        ILauncherLogger logger,
        IMicrosoftAuthService? microsoftAuth = null,
        IInstalledVersionScanner? installedScanner = null,
        IMinecraftInstallationLocator? installationLocator = null,
        ILauncherProfilesStore? profilesStore = null,
        IServersStore? serversStore = null,
        INewsClient? newsClient = null,
        IInstanceStore? instanceStore = null,
        IServerPinger? serverPinger = null,
        IJavaRuntimeManager? javaRuntimeManager = null,
        IModLoaderInstaller? modLoaderInstaller = null,
        FileCache? versionManifestCache = null,
        TimeSpan? versionManifestTtl = null,
        Lazy<IJavaRuntimeManager>? lazyJavaRuntimeManager = null,
        Lazy<IModLoaderInstaller>? lazyModLoaderInstaller = null)
    {
        _underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _microsoftAuth = microsoftAuth;
        _installedScanner = installedScanner ?? new FileSystemInstalledVersionScanner();
        _installationLocator = installationLocator ?? new DefaultMinecraftInstallationLocator();
        _profilesStore = profilesStore ?? new FileLauncherProfilesStore();
        _serversStore = serversStore ?? new FileServersStore();
        _serverPinger = serverPinger ?? new TcpServerPinger();
        _newsClient = newsClient ?? new MojangNewsClient();
        _instanceStore = instanceStore ?? new FileInstanceStore();
        _javaRuntimeManager = javaRuntimeManager ?? new NullJavaRuntimeManager();
        _modLoaderInstaller = modLoaderInstaller;
        _lazyJavaRuntimeManager = lazyJavaRuntimeManager;
        _lazyModLoaderInstaller = lazyModLoaderInstaller;
        _versionManifestCache = versionManifestCache;
        _versionManifestTtl = versionManifestTtl ?? DefaultVersionManifestTtl;
    }

    /// <summary>
    /// Convenience factory: wire a real <see cref="CmlLibUnderlyingLauncher"/> with the platform-default
    /// <c>.minecraft</c> directory. Used by the App's manual DI in <c>App.axaml.cs</c>.
    /// </summary>
    public static CmlLibMinecraftLauncherService Create(ILauncherLogger logger, IMicrosoftAuthService? microsoftAuth = null)
        => new(new CmlLibUnderlyingLauncher(), logger, microsoftAuth);

    /// <summary>
    /// Parse a <c>host</c> or <c>host:port</c> string (the shape stored in <c>servers.dat</c>) into
    /// its two parts. Empty / whitespace input throws <see cref="ArgumentException"/>; a missing
    /// port falls back to the Minecraft default (25565); an unparseable port preserves the
    /// numeric default rather than crashing.
    /// </summary>
    /// <param name="hostPort">e.g. <c>"mc.hypixel.net"</c>, <c>"127.0.0.1:25577"</c>, or an IPv6 bracketed form.</param>
    /// <returns>The host (lowercased trim) and TCP port.</returns>
    public static (string Host, int Port) ParseHostPort(string hostPort)
    {
        if (string.IsNullOrWhiteSpace(hostPort))
            throw new ArgumentException("host:port is required", nameof(hostPort));

        var trimmed = hostPort.Trim();
        const int DefaultPort = 25565;

        // IPv6 in brackets: [::1]:25565 or [::1]
        if (trimmed.StartsWith('['))
        {
            var close = trimmed.IndexOf(']');
            if (close > 0)
            {
                var host = trimmed.Substring(1, close - 1);
                if (close + 1 < trimmed.Length && trimmed[close + 1] == ':'
                    && int.TryParse(trimmed.AsSpan(close + 2), out var p6) && p6 is > 0 and <= 65535)
                    return (host, p6);
                return (host, DefaultPort);
            }
        }

        // Plain host[:port].
        var idx = trimmed.LastIndexOf(':');
        if (idx <= 0 || idx == trimmed.Length - 1)
            return (trimmed, DefaultPort);

        var hostPart = trimmed.Substring(0, idx);
        var portPart = trimmed.Substring(idx + 1);
        if (int.TryParse(portPart, out var port) && port is > 0 and <= 65535)
            return (hostPart, port);

        return (hostPart, DefaultPort);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NewsEntry>> ListNewsAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Fetching Mojang launcher news feed.");
        var news = await _newsClient.FetchAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info($"Loaded {news.Count} news entries.");
        return news;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Instance>> ListInstancesAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Loading Hyperion instances ...");
        var instances = await _instanceStore.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info($"Loaded {instances.Count} instances.");
        return instances;
    }

    /// <inheritdoc />
    public async Task SaveInstanceAsync(Instance instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        _logger.Info($"Saving instance '{instance.Name}' (id={instance.Id}, version={instance.VersionId}).");
        await _instanceStore.SaveAsync(instance, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteInstanceAsync(string id, CancellationToken cancellationToken)
    {
        _logger.Info($"Deleting instance id={id}.");
        await _instanceStore.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServerListEntry>> ListServersAsync(CancellationToken cancellationToken)
    {
        var install = _installationLocator.Locate();
        _logger.Info($"Reading servers.dat from '{install.ServersDatPath}'.");
        var servers = await _serversStore.LoadAsync(install.ServersDatPath, cancellationToken).ConfigureAwait(false);
        _logger.Info($"Loaded {servers.Count} servers.");
        return servers;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, ServerStatus?>> PingServersAsync(
        IEnumerable<ServerListEntry> entries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var list = entries as IReadOnlyList<ServerListEntry> ?? new List<ServerListEntry>(entries);
        if (list.Count == 0)
            return new Dictionary<string, ServerStatus?>(0);

        _logger.Info($"Pinging {list.Count} servers ...");
        var timeout = TimeSpan.FromSeconds(3);

        // We key by the entry's raw Ip string so the UI can look results up by reference identity
        // of what the user actually sees. Duplicate IPs in servers.dat win their first slot.
        var tasks = new Task<(string ip, ServerStatus? status)>[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            var entry = list[i];
            tasks[i] = PingOneAsync(entry, timeout, cancellationToken);
        }
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var dict = new Dictionary<string, ServerStatus?>(list.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (ip, status) in results)
        {
            if (string.IsNullOrEmpty(ip)) continue;
            dict[ip] = status;
        }
        var reachable = 0;
        foreach (var s in dict.Values) if (s is not null) reachable++;
        _logger.Info($"Pinged {dict.Count} servers ({reachable} reachable).");
        return dict;
    }

    private async Task<(string ip, ServerStatus? status)> PingOneAsync(
        ServerListEntry entry,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var ip = entry.Ip ?? string.Empty;
        var (host, port) = ParseHostPort(ip);
        if (string.IsNullOrEmpty(host))
            return (ip, null);
        try
        {
            var status = await _serverPinger.PingAsync(host, port, timeout, ct).ConfigureAwait(false);
            return (ip, status);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The pinger is meant to return null on failure, but anything that escapes shouldn't
            // bring the whole batch down - log it and surface "unreachable" for that one entry.
            _logger.Warn($"Ping failed for '{ip}': {ex.Message}");
            return (ip, null);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LauncherProfile>> ListProfilesAsync(CancellationToken cancellationToken)
    {
        var install = _installationLocator.Locate();
        _logger.Info($"Reading launcher_profiles.json from '{install.LauncherProfilesPath}'.");
        try
        {
            var file = await _profilesStore.LoadAsync(install.LauncherProfilesPath, cancellationToken).ConfigureAwait(false);
            _logger.Info($"Loaded {file.Profiles.Count} profiles (schema v{file.SchemaVersion}).");
            return file.Profiles;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // launcher_profiles.json can be malformed if the user's been editing it by hand;
            // surface that as an empty list rather than a hard error.
            _logger.Warn($"Could not parse launcher_profiles.json: {ex.Message}");
            return Array.Empty<LauncherProfile>();
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InstalledVersion>> ListInstalledVersionsAsync(CancellationToken cancellationToken)
    {
        var install = _installationLocator.Locate();
        _logger.Info($"Scanning installed versions under '{install.VersionsDirectory}'.");
        // The scan is a few file reads of small JSON manifests; wrap in Task.Run so the UI thread
        // never blocks on disk IO even though IInstalledVersionScanner is synchronous.
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<InstalledVersion> list = _installedScanner.Scan(install.VersionsDirectory);
            _logger.Info($"Found {list.Count} installed versions.");
            return list;
        }, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// v0.32.1 (T-startup-perf): consults <see cref="FileCache"/> first when one was injected.
    /// A fresh entry (younger than the configured TTL, default 30 min) is returned without
    /// hitting Mojang's CDN -- this is the path that shaves ~1.5 s off the cold start. On
    /// network failure we fall back to any stale cached copy so the version picker stays
    /// populated offline. Tests that don't pass a cache get the original network-only path.
    /// </remarks>
    public async Task<IReadOnlyList<VersionMetadata>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Listing Minecraft versions ...");

        // Fresh-cache fast path. The whole point of the 30-min TTL: a cold launcher start
        // within the window pays disk I/O (a few ms) instead of the ~1.5 s manifest round-trip.
        if (_versionManifestCache is not null)
        {
            var cached = await _versionManifestCache
                .TryReadAsync(VersionManifestCacheKey, _versionManifestTtl, cancellationToken)
                .ConfigureAwait(false);
            if (cached is { Length: > 0 })
            {
                var parsed = TryDeserialiseManifest(cached);
                if (parsed is not null)
                {
                    _logger.Info($"Loaded {parsed.Count} versions from disk cache (fresh).");
                    return parsed;
                }
            }
        }

        try
        {
            var list = await _underlying.GetAllVersionsAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info($"Loaded {list.Count} versions from the manifest.");
            // Best-effort writeback: a failed cache write must not abort a successful manifest fetch.
            if (_versionManifestCache is not null && list.Count > 0)
            {
                try
                {
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(list);
                    await _versionManifestCache
                        .WriteAsync(VersionManifestCacheKey, bytes, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception cacheEx)
                {
                    _logger.Warn($"Could not persist version manifest cache: {cacheEx.Message}");
                }
            }
            return list;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            // Offline / DNS / 5xx: serve a stale cached copy if we have one so the version
            // picker still has data. Matches the news-client's stale-on-failure behaviour.
            if (_versionManifestCache is not null)
            {
                var stale = await _versionManifestCache
                    .TryReadAsync(VersionManifestCacheKey, maxAge: null, cancellationToken)
                    .ConfigureAwait(false);
                if (stale is { Length: > 0 })
                {
                    var parsed = TryDeserialiseManifest(stale);
                    if (parsed is not null)
                    {
                        _logger.Warn($"Manifest fetch failed ({ex.Message}); serving {parsed.Count} stale cached versions.");
                        return parsed;
                    }
                }
            }
            var wrapped = new InstallationFailedException("Could not fetch the Minecraft version manifest (network error).", ex);
            _logger.Error("ListVersionsAsync failed (network).", wrapped);
            throw wrapped;
        }
        catch (Exception ex)
        {
            var wrapped = new InstallationFailedException("Could not fetch the Minecraft version manifest.", ex);
            _logger.Error("ListVersionsAsync failed.", wrapped);
            throw wrapped;
        }
    }

    /// <summary>
    /// Parse a previously-cached JSON manifest blob back into a list. Returns <c>null</c> for any
    /// corruption / schema drift so the caller falls through to a network re-fetch.
    /// </summary>
    private static IReadOnlyList<VersionMetadata>? TryDeserialiseManifest(byte[] bytes)
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<VersionMetadata>>(bytes);
            return list is { Count: > 0 } ? list : null;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<AuthResult> AuthenticateAsync(AuthRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // For Microsoft mode the username comes from the OAuth-issued profile, not the request.
        // We only require a username for offline play.
        if (request.Mode == AuthMode.Offline && string.IsNullOrWhiteSpace(request.Username))
        {
            var ex = new AuthenticationFailedException("Username is required for offline authentication.");
            _logger.Error("AuthenticateAsync rejected: empty username (offline).", ex);
            throw ex;
        }

        switch (request.Mode)
        {
            case AuthMode.Offline:
                var session = MSession.CreateOfflineSession(request.Username);
                _logger.Info($"Offline session created for '{request.Username}'.");
                return new AuthResult
                {
                    Username = session.Username ?? request.Username,
                    Uuid = session.UUID ?? string.Empty,
                    AccessToken = session.AccessToken ?? string.Empty,
                    IsOffline = true,
                };

            case AuthMode.Microsoft:
            {
                if (_microsoftAuth is null)
                {
                    var ex = new AuthenticationFailedException(
                        "Microsoft sign-in is not configured: no IMicrosoftAuthService was injected. " +
                        "The desktop App wires this up via MicrosoftAuthService; headless / test contexts must " +
                        "supply their own implementation or use AuthMode.Offline.");
                    _logger.Error("AuthenticateAsync rejected: Microsoft auth not configured.", ex);
                    throw ex;
                }

                _logger.Info("Microsoft sign-in: delegating to IMicrosoftAuthService.");
                try
                {
                    var result = await _microsoftAuth.SignInAsync(cancellationToken).ConfigureAwait(false);
                    _logger.Info($"Microsoft sign-in succeeded for '{result.Username}'.");
                    return result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (AuthenticationFailedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var wrapped = new AuthenticationFailedException($"Microsoft sign-in failed: {ex.Message}", ex);
                    _logger.Error("Microsoft sign-in failed.", wrapped);
                    throw wrapped;
                }
            }

            case AuthMode.Mojang:
            {
                var ex = new AuthenticationFailedException(
                    "Mojang username+password authentication was discontinued by Mojang in 2022 " +
                    "and is not supported by this launcher. Use AuthMode.Microsoft or AuthMode.Offline.");
                _logger.Error("AuthenticateAsync rejected: Mojang auth removed upstream.", ex);
                throw ex;
            }

            default:
            {
                var ex = new AuthenticationFailedException($"Unknown AuthMode '{request.Mode}'.");
                _logger.Error("AuthenticateAsync rejected: unknown mode.", ex);
                throw ex;
            }
        }
    }

    /// <inheritdoc />
    public async Task<LaunchResult> LaunchAsync(
        LaunchRequest request,
        IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Session);

        if (string.IsNullOrWhiteSpace(request.VersionName))
        {
            var ex = new VersionNotFoundException(request.VersionName ?? string.Empty);
            _logger.Error("LaunchAsync rejected: empty version name.", ex);
            throw ex;
        }

        _logger.Info($"Installing Minecraft '{request.VersionName}' for user '{request.Session.Username}' ...");

        // T21a (v0.30.0): mod-loader install runs before everything else. The installer
        // returns a modded version-id (e.g. "1.21.5-fabric-0.16.10") which we then feed to
        // the regular CmlLib install pipeline below - that path will download the loader's
        // library set defined in the new version's JSON manifest. The block is skipped for
        // vanilla (Loader == None) and for builds with no ModLoaderInstaller configured.
        // v0.32.1: resolved through the lazy property so first .Value triggers Adoptium / loader-installer
        // construction at launch time, never on cold startup.
        var modLoaderInstaller = ModLoaderInstaller;
        if (request.Loader != ModLoader.None && modLoaderInstaller is not null)
        {
            var loaderLabel = string.IsNullOrEmpty(request.LoaderVersion)
                ? $"{request.Loader} (latest)"
                : $"{request.Loader} {request.LoaderVersion}";
            var stage = $"Installing {loaderLabel}";
            progress?.Report(new LaunchProgress { Stage = stage, Fraction = 0 });
            _logger.Info($"Mod loader: {stage} on top of {request.VersionName} ...");
            try
            {
                var loaderProgress = progress is null ? null : new Progress<double>(f =>
                    progress.Report(new LaunchProgress { Stage = stage, Fraction = f }));
                var moddedId = await modLoaderInstaller
                    .InstallAsync(request.Loader, request.VersionName, request.LoaderVersion, loaderProgress, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrEmpty(moddedId) && !string.Equals(moddedId, request.VersionName, StringComparison.Ordinal))
                {
                    _logger.Info($"Mod loader installed: '{request.VersionName}' -> '{moddedId}'.");
                    request = request with { VersionName = moddedId };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (NotSupportedException ex)
            {
                var wrapped = new InstallationFailedException(
                    $"Mod loader '{request.Loader}' is not supported by this launcher build: {ex.Message}", ex);
                _logger.Error($"Loader install failed (unsupported) for '{request.VersionName}'.", wrapped);
                throw wrapped;
            }
            catch (HttpRequestException ex)
            {
                var wrapped = new InstallationFailedException(
                    $"Network error while installing {request.Loader}. Check your connection and retry.", ex);
                _logger.Error($"Loader install failed (network) for '{request.VersionName}'.", wrapped);
                throw wrapped;
            }
            catch (LauncherException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var wrapped = new InstallationFailedException(
                    $"Unexpected error while installing {request.Loader}: {ex.Message}", ex);
                _logger.Error($"Loader install failed for '{request.VersionName}'.", wrapped);
                throw wrapped;
            }
        }

        // Auto-download the matching Adoptium Temurin JRE if the request specifies a requirement
        // AND no explicit JavaPath was already set. We patch the request in place via 'with' so
        // CmlLibUnderlyingLauncher can pick up the resolved exe through MLaunchOption.JavaPath.
        if (request.JavaRequirement is JavaRequirement requirement && string.IsNullOrWhiteSpace(request.JavaPath))
        {
            progress?.Report(new LaunchProgress { Stage = $"Preparing Java {(int)requirement}", Fraction = 0 });
            try
            {
                var javaProgress = progress is null ? null : new Progress<double>(f =>
                    progress.Report(new LaunchProgress { Stage = $"Downloading Java", Fraction = f }));
                var javaPath = await JavaRuntimeManager
                    .EnsureRuntimeAsync(requirement, javaProgress, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(javaPath))
                {
                    _logger.Info($"Resolved managed JRE for {requirement} at '{javaPath}'.");
                    request = request with { JavaPath = javaPath };
                }
                else
                {
                    _logger.Info($"Java runtime manager returned no path for {requirement}; using launcher default.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // JRE bootstrap is a best-effort: log and proceed with whatever java the OS exposes.
                _logger.Warn($"Could not provision Java {requirement}: {ex.Message}. Falling back to system default.");
            }
        }

        try
        {
            await _underlying.InstallAsync(request.VersionName, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KeyNotFoundException ex)
        {
            var wrapped = new VersionNotFoundException(request.VersionName, ex);
            _logger.Error($"Version '{request.VersionName}' not found during install.", wrapped);
            throw wrapped;
        }
        catch (HttpRequestException ex)
        {
            var wrapped = new InstallationFailedException(
                $"Network error while installing '{request.VersionName}'. Check your connection and retry.", ex);
            _logger.Error($"Install failed (network) for '{request.VersionName}'.", wrapped);
            throw wrapped;
        }
        catch (IOException ex)
        {
            var wrapped = new InstallationFailedException(
                $"I/O error while installing '{request.VersionName}'. The destination directory may be locked or full.", ex);
            _logger.Error($"Install failed (I/O) for '{request.VersionName}'.", wrapped);
            throw wrapped;
        }
        catch (UnauthorizedAccessException ex)
        {
            var wrapped = new InstallationFailedException(
                $"Permission denied while installing '{request.VersionName}'. Try running as a normal user or change the game directory.", ex);
            _logger.Error($"Install failed (permissions) for '{request.VersionName}'.", wrapped);
            throw wrapped;
        }
        catch (LauncherException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var wrapped = new InstallationFailedException(
                $"Unexpected error while installing '{request.VersionName}': {ex.Message}", ex);
            _logger.Error($"Install failed for '{request.VersionName}'.", wrapped);
            throw wrapped;
        }

        progress?.Report(new LaunchProgress { Stage = "Starting game" });
        _logger.Info($"Starting Minecraft '{request.VersionName}' ...");

        try
        {
            var started = await _underlying
                .StartProcessAsync(request, request.CaptureGameLog, cancellationToken)
                .ConfigureAwait(false);

            _logger.Info($"Minecraft '{request.VersionName}' started (pid {started.ProcessId}).");
            return new LaunchResult
            {
                ProcessId = started.ProcessId,
                VersionName = request.VersionName,
                GameLogStream = started.GameLogStream,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            var wrapped = new GameProcessStartException(
                $"Could not start the Minecraft Java process. Is Java installed and on PATH? Underlying message: {ex.Message}", ex);
            _logger.Error($"Process start failed for '{request.VersionName}'.", wrapped);
            throw wrapped;
        }
        catch (FileNotFoundException ex)
        {
            var wrapped = new GameProcessStartException(
                $"Could not start Minecraft - a required file is missing: {ex.FileName ?? ex.Message}", ex);
            _logger.Error($"Process start failed (file missing) for '{request.VersionName}'.", wrapped);
            throw wrapped;
        }
        catch (LauncherException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var wrapped = new GameProcessStartException(
                $"Unexpected error starting Minecraft '{request.VersionName}': {ex.Message}", ex);
            _logger.Error($"Process start failed for '{request.VersionName}'.", wrapped);
            throw wrapped;
        }
    }
}
