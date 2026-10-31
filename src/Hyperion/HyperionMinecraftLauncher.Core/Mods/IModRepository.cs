using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods;

/// <summary>
/// Abstract source of mods (Modrinth, CurseForge, future others). Implementations should
/// be safe to call concurrently and must not throw on empty results - return an empty list.
/// </summary>
public interface IModRepository
{
    /// <summary>Which source this repository represents - used by the Mods page toggle.</summary>
    ModSource Source { get; }

    /// <summary>Search the catalog. Returns up to <see cref="ModSearchQuery.Limit"/> hits.</summary>
    Task<IReadOnlyList<Mod>> SearchAsync(ModSearchQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// List downloadable files under a given <paramref name="modId"/>. Filtering by
    /// <paramref name="gameVersion"/> / <paramref name="loader"/> is optional but strongly
    /// recommended - the unfiltered list is often hundreds of versions long.
    /// </summary>
    Task<IReadOnlyList<ModFile>> ListFilesAsync(
        string modId,
        string? gameVersion,
        ModLoader? loader,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stream a file's bytes into <paramref name="destination"/>. Progress fractions are
    /// 0.0..1.0; absent when <c>Content-Length</c> is missing.
    /// </summary>
    Task DownloadAsync(
        ModFile file,
        Stream destination,
        System.IProgress<double>? progress,
        CancellationToken cancellationToken);
}
