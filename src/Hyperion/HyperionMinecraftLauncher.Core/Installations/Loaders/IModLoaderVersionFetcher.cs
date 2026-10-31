using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Installations.Loaders;

/// <summary>
/// Pulls the available loader versions for a chosen Minecraft version. Used by the
/// New Instance dialog to fill the loader-version ComboBox once the user picks a
/// non-Vanilla loader.
/// </summary>
public interface IModLoaderVersionFetcher
{
    /// <summary>
    /// Return the list of loader versions valid for <paramref name="minecraftVersion"/>,
    /// newest first (so the UI can default to the first entry).
    /// </summary>
    /// <param name="loader">The loader to query. <see cref="ModLoader.None"/> returns empty.</param>
    /// <param name="minecraftVersion">Vanilla MC id (e.g. <c>"1.21.5"</c>).</param>
    /// <param name="cancellationToken">Cancels the underlying HTTP probe.</param>
    /// <returns>Loader version strings (e.g. <c>"0.16.10"</c>, <c>"49.0.30"</c>). Empty when the
    /// loader is recognized but its fetcher is not configured in this build.</returns>
    Task<IReadOnlyList<string>> ListLoaderVersionsAsync(
        ModLoader loader,
        string minecraftVersion,
        CancellationToken cancellationToken);
}
