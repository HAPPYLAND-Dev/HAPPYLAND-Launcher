using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.CrashReports;

/// <summary>
/// Enumerates the <c>crash-reports/</c> directory under one instance's <c>gameDir</c>
/// and returns each parsed crash report newest-first. The "listener" name follows the
/// pattern of <c>IInstanceBrowser</c>: read-only scan of a per-instance subfolder.
/// </summary>
public interface ICrashReportListener
{
    /// <summary>
    /// Scan <c>&lt;gameDir&gt;/crash-reports/*.txt</c>, parse each entry, and return up to
    /// <paramref name="maxCount"/> reports ordered newest-first by <see cref="CrashReport.GeneratedAt"/>.
    /// </summary>
    Task<IReadOnlyList<CrashReport>> ListRecentAsync(
        Instance instance,
        int maxCount = 20,
        CancellationToken cancellationToken = default);
}
