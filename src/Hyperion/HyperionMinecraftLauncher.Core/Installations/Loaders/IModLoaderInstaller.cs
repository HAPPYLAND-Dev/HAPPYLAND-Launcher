using System;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.Loaders;

/// <summary>
/// Installs a mod loader (Forge / Fabric / Quilt / NeoForge) on top of a vanilla Minecraft
/// version. The returned string is the resulting version-id (e.g.
/// <c>"1.21.5-fabric-0.16.10"</c> or <c>"1.20.4-forge-49.0.30"</c>) which the launcher then
/// passes back to <c>LaunchRequest.VersionName</c> for the regular launch path.
/// </summary>
public interface IModLoaderInstaller
{
    /// <summary>
    /// Install <paramref name="loader"/> at the requested <paramref name="loaderVersion"/> on top
    /// of vanilla <paramref name="minecraftVersion"/>.
    /// </summary>
    /// <param name="loader">The loader to install. <see cref="ModLoader.None"/> short-circuits
    /// to "return the vanilla id unchanged".</param>
    /// <param name="minecraftVersion">Vanilla MC version id (e.g. <c>"1.21.5"</c>).</param>
    /// <param name="loaderVersion">
    /// Loader-specific version string. <c>null</c> means "latest stable" -- each adapter resolves
    /// the right default. For Forge / NeoForge this is the loader build number; for Fabric / Quilt
    /// it is the loader version (e.g. <c>"0.16.10"</c>).
    /// </param>
    /// <param name="progress">0..1 fraction reporter. <c>null</c> if the caller doesn't care.</param>
    /// <param name="cancellationToken">Cancels the install.</param>
    /// <returns>
    /// The version-id ready to launch -- the same string the underlying <c>.minecraft/versions/</c>
    /// folder will be named, so the caller can pass it back to <see cref="Launcher.LaunchRequest"/>.
    /// </returns>
    /// <exception cref="NotSupportedException">The loader is recognized but its installer is not
    /// yet wired up (e.g. <see cref="ModLoader.OptiFine"/>, <see cref="ModLoader.LegacyForge"/>).</exception>
    Task<string> InstallAsync(
        ModLoader loader,
        string minecraftVersion,
        string? loaderVersion,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}
