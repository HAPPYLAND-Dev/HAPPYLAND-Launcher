using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Updates;

/// <summary>
/// Check-only update probe. The launcher never auto-installs - the UI banner just
/// surfaces a "newer version available" hint and opens the release page on click.
/// </summary>
public interface IUpdateChecker
{
    /// <summary>
    /// Return an <see cref="UpdateInfo"/> if the remote feed reports a strictly newer release,
    /// otherwise <c>null</c>. Implementations must swallow transport errors (HTTP failures,
    /// schema drift, parse errors) and return <c>null</c> on any non-success - a broken update
    /// check must never break launcher startup.
    /// </summary>
    /// <param name="currentVersion">The running launcher's version (e.g. <c>"0.28.0"</c>).</param>
    /// <param name="cancellationToken">Cancellation token to bail out of the network call.</param>
    Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken cancellationToken);
}
