using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core.Installer.Forge.Versions;
using CmlLib.Core.Installer.NeoForge.Versions;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ModLoaders.QuiltMC;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.Loaders;

/// <summary>
/// Production adapter that pulls Forge build versions from CmlLib's
/// <see cref="ForgeVersionLoader"/>. Caches a single <see cref="HttpClient"/> per instance.
/// </summary>
public sealed class CmlLibForgeVersionFetcher : IUnderlyingForgeVersionFetcher
{
    private readonly HttpClient _http;

    public CmlLibForgeVersionFetcher(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListAsync(string minecraftVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var loader = new ForgeVersionLoader(_http);
        var versions = await loader.GetForgeVersions(minecraftVersion).ConfigureAwait(false);
        return versions
            .Select(v => v.ForgeVersionName ?? string.Empty)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }
}

/// <summary>
/// Production adapter for NeoForge via <see cref="NeoForgeVersionLoader"/>.
/// </summary>
public sealed class CmlLibNeoForgeVersionFetcher : IUnderlyingNeoForgeVersionFetcher
{
    private readonly HttpClient _http;

    public CmlLibNeoForgeVersionFetcher(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListAsync(string minecraftVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var loader = new NeoForgeVersionLoader(_http);
        var versions = await loader.GetNeoForgeVersions(minecraftVersion).ConfigureAwait(false);
        return versions
            .Select(v => v.VersionName ?? string.Empty)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }
}

/// <summary>
/// Production adapter for Fabric via <see cref="FabricInstaller.GetLoaders"/>.
/// </summary>
public sealed class CmlLibFabricVersionFetcher : IUnderlyingFabricVersionFetcher
{
    private readonly HttpClient _http;

    public CmlLibFabricVersionFetcher(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListAsync(string minecraftVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var installer = new FabricInstaller(_http);
        var loaders = await installer.GetLoaders(minecraftVersion).ConfigureAwait(false);
        return loaders
            .Select(l => l.Version ?? string.Empty)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }
}

/// <summary>
/// Production adapter for Quilt via <see cref="QuiltInstaller.GetLoaders"/>.
/// </summary>
public sealed class CmlLibQuiltVersionFetcher : IUnderlyingQuiltVersionFetcher
{
    private readonly HttpClient _http;

    public CmlLibQuiltVersionFetcher(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListAsync(string minecraftVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var installer = new QuiltInstaller(_http);
        var loaders = await installer.GetLoaders(minecraftVersion).ConfigureAwait(false);
        return loaders
            .Select(l => l.Version ?? string.Empty)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }
}
