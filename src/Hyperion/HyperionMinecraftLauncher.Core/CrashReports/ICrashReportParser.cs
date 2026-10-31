using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.CrashReports;

/// <summary>
/// Parses one Minecraft crash report text file into a structured <see cref="CrashReport"/>.
/// Implementations should never throw on malformed input; partial results with empty
/// suspect lists are preferred so the caller can still show the raw text.
/// </summary>
public interface ICrashReportParser
{
    /// <summary>Read and parse <paramref name="filePath"/>. The path is treated as opaque - the parser only uses the filename to extract a timestamp.</summary>
    Task<CrashReport> ParseAsync(string filePath, CancellationToken cancellationToken);
}
