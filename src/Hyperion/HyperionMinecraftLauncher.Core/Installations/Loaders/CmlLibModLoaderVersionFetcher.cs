using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.Loaders;

/// <summary>
/// Dispatcher <see cref="IModLoaderVersionFetcher"/> that routes each loader to its
/// underlying fetcher (Forge, Fabric, etc.). Unconfigured loaders return an empty list
/// rather than throwing - the UI can then show a "(loader not supported)" placeholder.
/// </summary>
public sealed class CmlLibModLoaderVersionFetcher : IModLoaderVersionFetcher
{
    private readonly IUnderlyingForgeVersionFetcher? _forge;
    private readonly IUnderlyingNeoForgeVersionFetcher? _neoForge;
    private readonly IUnderlyingFabricVersionFetcher? _fabric;
    private readonly IUnderlyingQuiltVersionFetcher? _quilt;

    public CmlLibModLoaderVersionFetcher(
        IUnderlyingForgeVersionFetcher? forge = null,
        IUnderlyingNeoForgeVersionFetcher? neoForge = null,
        IUnderlyingFabricVersionFetcher? fabric = null,
        IUnderlyingQuiltVersionFetcher? quilt = null)
    {
        _forge = forge;
        _neoForge = neoForge;
        _fabric = fabric;
        _quilt = quilt;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListLoaderVersionsAsync(
        ModLoader loader,
        string minecraftVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            throw new ArgumentException("minecraftVersion is required", nameof(minecraftVersion));

        IReadOnlyList<string> empty = Array.Empty<string>();
        switch (loader)
        {
            case ModLoader.None:
                return Task.FromResult(empty);

            case ModLoader.Forge:
                return _forge is null ? Task.FromResult(empty) : _forge.ListAsync(minecraftVersion, cancellationToken);

            case ModLoader.NeoForge:
                return _neoForge is null ? Task.FromResult(empty) : _neoForge.ListAsync(minecraftVersion, cancellationToken);

            case ModLoader.Fabric:
                return _fabric is null ? Task.FromResult(empty) : _fabric.ListAsync(minecraftVersion, cancellationToken);

            case ModLoader.Quilt:
                return _quilt is null ? Task.FromResult(empty) : _quilt.ListAsync(minecraftVersion, cancellationToken);

            default:
                return Task.FromResult(empty);
        }
    }
}
