using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Versions;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

/// <summary>
/// Test seam over the slice of <c>CmlLib.Core.MinecraftLauncher</c> we actually use.
/// Production code wires this to a thin CmlLib adapter; tests substitute a fake so
/// they never need a real Minecraft installation, network, or Java.
/// </summary>
public interface IUnderlyingLauncher
{
    /// <summary>Fetch the version manifest (release + snapshot list).</summary>
    Task<IReadOnlyList<VersionMetadata>> GetAllVersionsAsync(CancellationToken cancellationToken);

    /// <summary>Install (download + extract) the named version, surfacing progress through the provided <see cref="IProgress{T}"/>.</summary>
    Task InstallAsync(
        string versionName,
        IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Spawn the Java process for the given <paramref name="request"/>, returning its OS process id
    /// and, when <paramref name="captureGameLog"/> is <c>true</c>, a live multicast
    /// <see cref="IObservable{T}"/> of <see cref="GameLogLine"/>s drained from the child's
    /// stdout/stderr pipes.
    /// </summary>
    /// <param name="request">
    /// All session, RAM, and Quick Play settings - resilient to future additions on
    /// <see cref="LaunchRequest"/> without further sig churn.
    /// </param>
    /// <param name="captureGameLog">
    /// When <c>true</c>, the implementation redirects <c>stdout</c>/<c>stderr</c> and exposes
    /// them through <see cref="StartProcessResult.GameLogStream"/>. When <c>false</c>, the
    /// implementation skips both the redirection cost and the observable plumbing - production
    /// launches with <see cref="Settings.LauncherSettings.ShowGameLog"/> off pay no per-line
    /// allocation overhead.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the launch.</param>
    Task<StartProcessResult> StartProcessAsync(
        LaunchRequest request,
        bool captureGameLog,
        CancellationToken cancellationToken);
}
