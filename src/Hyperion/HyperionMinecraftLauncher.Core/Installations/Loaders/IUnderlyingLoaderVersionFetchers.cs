using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.Loaders;

/// <summary>Test seam over CmlLib's <c>ForgeVersionLoader</c>. Returns ordered loader version strings.</summary>
public interface IUnderlyingForgeVersionFetcher
{
    Task<IReadOnlyList<string>> ListAsync(string minecraftVersion, CancellationToken cancellationToken);
}

/// <summary>Test seam over CmlLib's <c>NeoForgeVersionLoader</c>.</summary>
public interface IUnderlyingNeoForgeVersionFetcher
{
    Task<IReadOnlyList<string>> ListAsync(string minecraftVersion, CancellationToken cancellationToken);
}

/// <summary>Test seam over CmlLib's <c>FabricInstaller.GetLoaders</c>.</summary>
public interface IUnderlyingFabricVersionFetcher
{
    Task<IReadOnlyList<string>> ListAsync(string minecraftVersion, CancellationToken cancellationToken);
}

/// <summary>Test seam over CmlLib's <c>QuiltInstaller.GetLoaders</c>.</summary>
public interface IUnderlyingQuiltVersionFetcher
{
    Task<IReadOnlyList<string>> ListAsync(string minecraftVersion, CancellationToken cancellationToken);
}
