// Production-grade logger built on Serilog.
//
// NuGet choice: we use plain Serilog (Serilog + Serilog.Sinks.File +
// Serilog.Formatting.Compact) rather than Serilog.TechTeaStudio.Wrap. The Wrap
// package exists on nuget.org but its public LoggerOptions does not expose
// FileSizeLimitBytes, and we need the 32 MB per-file cap that
// Serilog.Sinks.File offers natively.
//
// Behavior:
//   - Daily-rotated file sink at <logDirectory>/launcher-.log (Serilog inserts the
//     local date before the extension - launcher-yyyyMMdd.log, see LauncherLogFiles).
//   - 14-day retention via RetainedFileCountLimit = 14.
//   - 32 MB per-file cap via FileSizeLimitBytes = 32 * 1024 * 1024 + rollOnFileSizeLimit.
//   - JSON output via CompactJsonFormatter (one structured event per line).
//
// Dispose flushes pending writes and disposes the underlying Logger.
using System;
using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Formatting.Compact;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

/// <summary>
/// Serilog-backed implementation of <see cref="ILauncherLogger"/>. Writes one structured
/// JSON event per line to a daily-rotated file under the configured directory. Disposing
/// the logger flushes pending writes and releases the underlying Serilog pipeline.
/// </summary>
public sealed class SerilogLauncherLogger : ILauncherLogger, IDisposable
{
    private const long FileSizeLimitBytes = 32L * 1024L * 1024L; // 32 MB
    private const int RetainedFileCountLimit = 14;               // 14 day retention

    private readonly string _logDirectory;
    private readonly Logger _logger;
    private int _disposed;

    /// <summary>
    /// Construct a Serilog logger that writes under <paramref name="logDirectory"/>.
    /// The directory is created eagerly if it does not already exist.
    /// </summary>
    /// <param name="logDirectory">Target directory for log files. Must be non-empty.</param>
    public SerilogLauncherLogger(string logDirectory)
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
            throw new ArgumentException("logDirectory is required", nameof(logDirectory));

        _logDirectory = logDirectory;
        // Create the directory eagerly so callers that probe the filesystem right after
        // construction (tests, diagnostic dumps) see it. The sink would also create it
        // lazily on first write, but eager creation matches the FileLauncherLogger contract.
        Directory.CreateDirectory(_logDirectory);

        var path = Path.Combine(_logDirectory, "launcher-.log");

        _logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                formatter: new CompactJsonFormatter(),
                path: path,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFileCountLimit,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                shared: false)
            .CreateLogger();
    }

    /// <inheritdoc />
    public void Info(string message) => _logger.Information("{Message}", message ?? string.Empty);

    /// <inheritdoc />
    public void Warn(string message) => _logger.Warning("{Message}", message ?? string.Empty);

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null)
        => _logger.Error(exception, "{Message}", message ?? string.Empty);

    /// <summary>
    /// Flush any pending log events and release the underlying Serilog pipeline.
    /// Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) == 1)
            return;
        // Logger.Dispose flushes the sink chain - mirrors what Serilog's Log.CloseAndFlush()
        // does for the global Log.Logger, but scoped to this instance only.
        _logger.Dispose();
    }
}
