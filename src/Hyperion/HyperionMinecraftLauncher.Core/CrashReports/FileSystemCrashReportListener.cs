using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.CrashReports;

/// <summary>
/// File-system-backed <see cref="ICrashReportListener"/>. Wraps a single
/// <see cref="ICrashReportParser"/>; resolves the per-instance root to
/// <see cref="Instance.GameDirectory"/> when set, else the platform default
/// <c>.minecraft</c>. Scans <c>&lt;root&gt;/crash-reports/*.txt</c>.
/// </summary>
public sealed class FileSystemCrashReportListener : ICrashReportListener
{
    private readonly ICrashReportParser _parser;

    /// <summary>Default ctor - uses the built-in <see cref="MinecraftCrashReportParser"/>.</summary>
    public FileSystemCrashReportListener() : this(new MinecraftCrashReportParser())
    {
    }

    /// <summary>Test-friendly ctor: inject a fake parser.</summary>
    public FileSystemCrashReportListener(ICrashReportParser parser)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CrashReport>> ListRecentAsync(
        Instance instance,
        int maxCount = 20,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (maxCount <= 0) return Array.Empty<CrashReport>();

        var root = ResolveRoot(instance);
        var dir = Path.Combine(root, "crash-reports");
        if (!Directory.Exists(dir)) return Array.Empty<CrashReport>();

        // Pre-sort by mtime so we don't bother parsing the whole pile when maxCount is small.
        var files = Directory.EnumerateFiles(dir, "*.txt", SearchOption.TopDirectoryOnly)
            .Select(p => new FileInfo(p))
            .OrderByDescending(fi => fi.LastWriteTimeUtc)
            .Take(maxCount)
            .ToArray();

        if (files.Length == 0) return Array.Empty<CrashReport>();

        var reports = new List<CrashReport>(files.Length);
        foreach (var fi in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var report = await _parser.ParseAsync(fi.FullName, cancellationToken).ConfigureAwait(false);
                reports.Add(report);
            }
            catch
            {
                // Never let one bad file stop the whole list.
            }
        }

        // Re-sort by parsed GeneratedAt - the filename embeds the real timestamp, which is
        // a more truthful "when did Minecraft crash" than the file's mtime on some platforms.
        return reports
            .OrderByDescending(r => r.GeneratedAt)
            .ToArray();
    }

    private static string ResolveRoot(Instance instance)
    {
        return !string.IsNullOrWhiteSpace(instance.GameDirectory)
            ? instance.GameDirectory
            : DefaultMinecraftInstallationLocator.ResolveRoot();
    }
}
