using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

/// <summary>
/// A do-nothing <see cref="IJavaRuntimeManager"/> for tests and headless contexts where the
/// real Adoptium download must not run. <see cref="EnsureRuntimeAsync"/> returns
/// <see cref="string.Empty"/>, signalling "no managed runtime - let the launcher pick whatever
/// java is on PATH".
/// </summary>
public sealed class NullJavaRuntimeManager : IJavaRuntimeManager
{
    /// <inheritdoc />
    public Task<string> EnsureRuntimeAsync(
        JavaRequirement requirement,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(string.Empty);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InstalledJavaRuntime>> ListInstalledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<InstalledJavaRuntime> empty = Array.Empty<InstalledJavaRuntime>();
        return Task.FromResult(empty);
    }

    /// <inheritdoc />
    public Task RemoveAsync(JavaRequirement requirement, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
