using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

/// <summary>
/// Per-launcher manager for downloaded Adoptium Temurin JREs. Implementations decide
/// where the runtimes live on disk and how to bootstrap one for a given
/// <see cref="JavaRequirement"/>; the launcher service calls
/// <see cref="EnsureRuntimeAsync"/> right before invoking the underlying CmlLib launch.
/// </summary>
public interface IJavaRuntimeManager
{
    /// <summary>
    /// Ensure a Java runtime matching <paramref name="requirement"/> exists on disk; download
    /// and extract it if necessary. Returns the absolute path to the resolved
    /// <c>java</c> / <c>java.exe</c> executable so the caller can set <c>JavaPath</c> on the
    /// underlying launcher.
    /// </summary>
    /// <param name="requirement">Which Java family the launching Minecraft version needs.</param>
    /// <param name="progress">Optional progress receiver, reporting 0..1 fraction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> EnsureRuntimeAsync(
        JavaRequirement requirement,
        IProgress<double>? progress,
        CancellationToken cancellationToken);

    /// <summary>List every Java runtime currently installed under the manager's root.</summary>
    Task<IReadOnlyList<InstalledJavaRuntime>> ListInstalledAsync(CancellationToken cancellationToken);

    /// <summary>Delete the installed runtime for the given requirement, if present.</summary>
    Task RemoveAsync(JavaRequirement requirement, CancellationToken cancellationToken);
}
