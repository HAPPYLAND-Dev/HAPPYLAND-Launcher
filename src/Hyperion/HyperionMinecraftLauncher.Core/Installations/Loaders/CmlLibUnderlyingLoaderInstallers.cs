using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core;
using CmlLib.Core.Installers;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.Forge.Versions;
using System.Linq;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.Installer.NeoForge.Installers;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ModLoaders.QuiltMC;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.Loaders;

/// <summary>
/// Production adapter that delegates to CmlLib's <c>ForgeInstaller</c>. Owns no state beyond
/// the underlying <see cref="MinecraftLauncher"/> handle, so it's safe to construct one per
/// install call. Translates the 0..1 fraction progress contract onto CmlLib's two-channel
/// (file/byte) progress shape.
/// </summary>
public sealed class CmlLibForgeUnderlying : IUnderlyingForgeInstaller
{
    private readonly MinecraftLauncher _launcher;
    public string? JavaPath { get; set; }

    /// <summary>Wraps the supplied <see cref="MinecraftLauncher"/> -- typically the same instance the rest of the launcher service uses.</summary>
    public CmlLibForgeUnderlying(MinecraftLauncher launcher)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    /// <inheritdoc />
    public async Task<string> InstallAsync(string minecraftVersion, string? forgeVersion, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        var versions = await new ForgeVersionLoader(http).GetForgeVersions(minecraftVersion);
        var selected = string.IsNullOrWhiteSpace(forgeVersion)
            ? versions.FirstOrDefault(v => v.IsRecommendedVersion) ?? versions.FirstOrDefault(v => v.IsLatestVersion) ?? versions.FirstOrDefault()
            : versions.FirstOrDefault(v => v.ForgeVersionName == forgeVersion);
        if (selected is null) throw new InvalidOperationException($"找不到 Forge 版本 {forgeVersion}。");
        var installer = new ForgeInstallerVersionMapper().CreateInstaller(selected);
        var versionJson = Path.Combine(_launcher.MinecraftPath.Versions, installer.VersionName, installer.VersionName + ".json");
        var completionMarker = Path.Combine(_launcher.MinecraftPath.Versions, installer.VersionName, ".happyland-installed");
        if (File.Exists(versionJson) && File.Exists(completionMarker)) return installer.VersionName;
        var options = new ForgeInstallOptions
        {
            SkipIfAlreadyInstalled = true,
            JavaPath = JavaPath,
            CancellationToken = cancellationToken,
            FileProgress = WrapFileProgress(progress),
            ByteProgress = WrapByteProgress(progress),
        };
        var version = await _launcher.GetVersionAsync(minecraftVersion, cancellationToken);
        await _launcher.InstallAsync(version, options.FileProgress, options.ByteProgress, cancellationToken);
        await installer.Install(_launcher.MinecraftPath, _launcher.GameInstaller, options);
        await _launcher.GetAllVersionsAsync(cancellationToken);
        await File.WriteAllTextAsync(completionMarker, installer.VersionName, cancellationToken);
        return installer.VersionName;
    }

    internal static IProgress<InstallerProgressChangedEventArgs>? WrapFileProgress(IProgress<double>? progress)
    {
        if (progress is null) return null;
        return new SyncProgress<InstallerProgressChangedEventArgs>(args =>
        {
            if (args.TotalTasks > 0)
                progress.Report((double)args.ProgressedTasks / args.TotalTasks);
        });
    }

    internal static IProgress<ByteProgress>? WrapByteProgress(IProgress<double>? progress)
    {
        if (progress is null) return null;
        return new SyncProgress<ByteProgress>(args =>
        {
            if (args.TotalBytes > 0)
                progress.Report((double)args.ProgressedBytes / args.TotalBytes);
        });
    }
}

/// <summary>
/// Production adapter for CmlLib's <c>NeoForgeInstaller</c>. Mirrors <see cref="CmlLibForgeUnderlying"/>.
/// </summary>
public sealed class CmlLibNeoForgeUnderlying : IUnderlyingNeoForgeInstaller
{
    private readonly MinecraftLauncher _launcher;
    public string? JavaPath { get; set; }

    public CmlLibNeoForgeUnderlying(MinecraftLauncher launcher)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    /// <inheritdoc />
    public Task<string> InstallAsync(string minecraftVersion, string? neoForgeVersion, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var installer = new NeoForgeInstaller(_launcher);
        var options = new NeoForgeInstallOptions
        {
            SkipIfAlreadyInstalled = true,
            JavaPath = JavaPath,
            CancellationToken = cancellationToken,
            FileProgress = CmlLibForgeUnderlying.WrapFileProgress(progress),
            ByteProgress = CmlLibForgeUnderlying.WrapByteProgress(progress),
        };

        return string.IsNullOrWhiteSpace(neoForgeVersion)
            ? installer.Install(minecraftVersion, options)
            : installer.Install(minecraftVersion, neoForgeVersion!, options);
    }
}

/// <summary>
/// Production adapter for CmlLib's <c>FabricInstaller</c>. Fabric installs are pure
/// JSON+library writes (no installer JAR + bootstrap dance), so the progress shape is
/// just "started" / "done"; we forward 0.0 at start and 1.0 at the end.
/// </summary>
public sealed class CmlLibFabricUnderlying : IUnderlyingFabricInstaller
{
    private readonly HttpClient _http;
    private readonly MinecraftLauncher _launcher;

    public CmlLibFabricUnderlying(HttpClient http, MinecraftLauncher launcher)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    /// <inheritdoc />
    public async Task<string> InstallAsync(string minecraftVersion, string? fabricLoaderVersion, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(0.0);

        var installer = new FabricInstaller(_http);
        // The MinecraftPath constructor reads the launcher's currently-configured game dir.
        // CmlLib's MinecraftPath property is the same one the regular install path uses.
        string id;
        if (string.IsNullOrWhiteSpace(fabricLoaderVersion))
        {
            id = await installer.Install(minecraftVersion, _launcher.MinecraftPath).ConfigureAwait(false);
        }
        else
        {
            id = await installer.Install(minecraftVersion, fabricLoaderVersion!, _launcher.MinecraftPath).ConfigureAwait(false);
        }

        progress?.Report(1.0);
        return id;
    }
}

/// <summary>
/// Production adapter for CmlLib's <c>QuiltInstaller</c>. Mirrors <see cref="CmlLibFabricUnderlying"/>.
/// </summary>
public sealed class CmlLibQuiltUnderlying : IUnderlyingQuiltInstaller
{
    private readonly HttpClient _http;
    private readonly MinecraftLauncher _launcher;

    public CmlLibQuiltUnderlying(HttpClient http, MinecraftLauncher launcher)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    /// <inheritdoc />
    public async Task<string> InstallAsync(string minecraftVersion, string? quiltLoaderVersion, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(0.0);

        var installer = new QuiltInstaller(_http);
        string id;
        if (string.IsNullOrWhiteSpace(quiltLoaderVersion))
        {
            id = await installer.Install(minecraftVersion, _launcher.MinecraftPath).ConfigureAwait(false);
        }
        else
        {
            id = await installer.Install(minecraftVersion, quiltLoaderVersion!, _launcher.MinecraftPath).ConfigureAwait(false);
        }

        progress?.Report(1.0);
        return id;
    }
}
