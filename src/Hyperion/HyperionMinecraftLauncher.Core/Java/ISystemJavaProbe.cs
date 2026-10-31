using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Java;

/// <summary>
/// Locates a Java runtime that is already installed on the host machine and matches a
/// given <see cref="JavaRequirement"/>. Implementations are queried by
/// <see cref="AdoptiumJavaRuntimeManager"/> *before* it falls back to downloading a fresh
/// Adoptium archive, so a user with a working JDK / JRE under JAVA_HOME, on PATH, or in
/// a standard install directory doesn't pay the cost of a second download.
/// </summary>
/// <remarks>
/// The probe must be **non-destructive** and **safe to call from a hot path** - it cannot
/// throw on individual candidate failures (a broken JDK install, a missing executable,
/// a hung <c>java -version</c>) and should silently skip them so a single bad candidate
/// doesn't poison the lookup. Implementations are also expected to be fast: the probe
/// runs synchronously on the launch path right before the JRE is needed.
/// </remarks>
public interface ISystemJavaProbe
{
    /// <summary>
    /// Look for an already-installed Java runtime that satisfies <paramref name="requirement"/>
    /// (exact major-version match). Returns the absolute path to <c>java(.exe)</c>, or
    /// <c>null</c> if no candidate matched.
    /// </summary>
    /// <param name="requirement">Which Java family the caller needs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string?> FindAsync(JavaRequirement requirement, CancellationToken cancellationToken);
}
