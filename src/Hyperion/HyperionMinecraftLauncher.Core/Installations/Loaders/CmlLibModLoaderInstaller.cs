using System;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.Loaders;

/// <summary>
/// Dispatcher <see cref="IModLoaderInstaller"/> that routes each <see cref="ModLoader"/> to its
/// matching CmlLib-backed underlying installer. The interfaces it depends on
/// (<see cref="IUnderlyingForgeInstaller"/>, etc.) are the same shape the CmlLib adapters
/// implement in production -- which keeps the dispatch logic tested without touching
/// the network, the disk, or Java.
/// </summary>
public sealed class CmlLibModLoaderInstaller : IModLoaderInstaller
{
    private readonly IUnderlyingForgeInstaller? _forge;
    private readonly IUnderlyingNeoForgeInstaller? _neoForge;
    private readonly IUnderlyingFabricInstaller? _fabric;
    private readonly IUnderlyingQuiltInstaller? _quilt;

    /// <summary>
    /// Wire underlying installers. Each one is optional: passing <c>null</c> means
    /// "that loader is not supported in this build" and any install request for it throws
    /// <see cref="NotSupportedException"/>. Tests typically wire only the loader under test.
    /// </summary>
    public CmlLibModLoaderInstaller(
        IUnderlyingForgeInstaller? forge = null,
        IUnderlyingNeoForgeInstaller? neoForge = null,
        IUnderlyingFabricInstaller? fabric = null,
        IUnderlyingQuiltInstaller? quilt = null)
    {
        _forge = forge;
        _neoForge = neoForge;
        _fabric = fabric;
        _quilt = quilt;
    }

    /// <inheritdoc />
    public Task<string> InstallAsync(
        ModLoader loader,
        string minecraftVersion,
        string? loaderVersion,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            throw new ArgumentException("minecraftVersion is required", nameof(minecraftVersion));

        switch (loader)
        {
            case ModLoader.None:
                // Vanilla: nothing to install, the caller can use minecraftVersion as-is.
                return Task.FromResult(minecraftVersion);

            case ModLoader.Forge:
                if (_forge is null) throw new NotSupportedException("Forge support is not configured in this build.");
                return _forge.InstallAsync(minecraftVersion, loaderVersion, progress, cancellationToken);

            case ModLoader.NeoForge:
                if (_neoForge is null) throw new NotSupportedException("NeoForge support is not configured in this build.");
                return _neoForge.InstallAsync(minecraftVersion, loaderVersion, progress, cancellationToken);

            case ModLoader.Fabric:
                if (_fabric is null) throw new NotSupportedException("Fabric support is not configured in this build.");
                return _fabric.InstallAsync(minecraftVersion, loaderVersion, progress, cancellationToken);

            case ModLoader.Quilt:
                if (_quilt is null) throw new NotSupportedException("Quilt support is not configured in this build.");
                return _quilt.InstallAsync(minecraftVersion, loaderVersion, progress, cancellationToken);

            case ModLoader.OptiFine:
                throw new NotSupportedException("OptiFine loader support is on the roadmap.");

            case ModLoader.LegacyForge:
                throw new NotSupportedException("Legacy Forge (1.7.10 era) loader support is on the roadmap.");

            case ModLoader.Other:
                throw new NotSupportedException("Unknown / 'Other' loader cannot be installed automatically.");

            default:
                throw new NotSupportedException($"Unrecognized loader '{loader}'.");
        }
    }
}
