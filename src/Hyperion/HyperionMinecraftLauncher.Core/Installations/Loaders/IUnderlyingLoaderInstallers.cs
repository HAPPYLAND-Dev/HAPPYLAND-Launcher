using System;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.Loaders;

/// <summary>
/// Test seam over CmlLib's <c>ForgeInstaller</c>. The production wiring delegates to the real
/// CmlLib type via <see cref="CmlLibForgeUnderlying"/>; tests substitute a fake so they don't
/// need a Maven mirror, a real <c>.minecraft</c>, or Java.
/// </summary>
public interface IUnderlyingForgeInstaller
{
    /// <summary>Install Forge for the given vanilla version. Returns the resulting version-id.</summary>
    Task<string> InstallAsync(string minecraftVersion, string? forgeVersion, IProgress<double>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Test seam over CmlLib's <c>NeoForgeInstaller</c>.
/// </summary>
public interface IUnderlyingNeoForgeInstaller
{
    /// <summary>Install NeoForge for the given vanilla version. Returns the resulting version-id.</summary>
    Task<string> InstallAsync(string minecraftVersion, string? neoForgeVersion, IProgress<double>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Test seam over CmlLib's <c>FabricInstaller</c>.
/// </summary>
public interface IUnderlyingFabricInstaller
{
    /// <summary>Install Fabric for the given vanilla version. Returns the resulting version-id.</summary>
    Task<string> InstallAsync(string minecraftVersion, string? fabricLoaderVersion, IProgress<double>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Test seam over CmlLib's <c>QuiltInstaller</c>.
/// </summary>
public interface IUnderlyingQuiltInstaller
{
    /// <summary>Install Quilt for the given vanilla version. Returns the resulting version-id.</summary>
    Task<string> InstallAsync(string minecraftVersion, string? quiltLoaderVersion, IProgress<double>? progress, CancellationToken cancellationToken);
}
