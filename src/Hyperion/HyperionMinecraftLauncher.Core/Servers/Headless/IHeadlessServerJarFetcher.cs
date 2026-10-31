using System;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;

/// <summary>
/// Resolves and downloads the dedicated <c>server.jar</c> for a given Minecraft version.
/// Implementations consult the public Mojang version manifest (the same one CmlLib uses for
/// the client jar) and write the binary into the target server folder. Sha1 verification is
/// mandatory so a half-finished download never gets booted; a cached jar whose hash matches
/// the manifest is reused verbatim and the call returns synchronously fast.
/// </summary>
public interface IHeadlessServerJarFetcher
{
    /// <summary>
    /// Ensure a verified <c>server.jar</c> for <paramref name="minecraftVersion"/> exists inside
    /// <paramref name="targetDirectory"/> and return its absolute path. Downloads when missing
    /// or when the existing file's sha1 differs from the manifest hash.
    /// </summary>
    /// <param name="minecraftVersion">Minecraft version id (e.g. <c>"1.21.5"</c>).</param>
    /// <param name="targetDirectory">Absolute path of the server's working directory.</param>
    /// <param name="progress">Optional 0..1 fraction receiver for the download stage.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> EnsureServerJarAsync(
        string minecraftVersion,
        string targetDirectory,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}
