using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

/// <summary>
/// Thread-safe, daily-rotated file logger.
///
/// Writes one line per call to <c>{LogDirectory}/launcher-YYYY-MM-DD.log</c>. The date stamp
/// comes from the injected clock, so tests can deterministically drive rotation. Each call
/// reopens the file in append mode under a coarse lock; this is fine for the launcher's
/// low log volume (a few dozen lines per launch).
/// </summary>
[Obsolete("Prefer SerilogLauncherLogger. Kept for deterministic tests with injected clock.", false)]
public sealed class FileLauncherLogger : ILauncherLogger, IDisposable
{
    private static readonly Encoding LogEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly string _logDirectory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _sync = new();

    /// <summary>
    /// Construct a file logger that writes under <paramref name="logDirectory"/>.
    /// If the directory doesn't exist, it is created.
    /// </summary>
    /// <param name="logDirectory">Target directory for log files.</param>
    /// <param name="clock">Optional clock; defaults to <see cref="DateTimeOffset.UtcNow"/>. Useful in tests.</param>
    public FileLauncherLogger(string logDirectory, Func<DateTimeOffset>? clock = null)
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
            throw new ArgumentException("logDirectory is required", nameof(logDirectory));

        _logDirectory = logDirectory;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        // Create eagerly so the directory exists by the time tests / callers inspect it -
        // we don't want a logger constructed at app start to silently fail later.
        Directory.CreateDirectory(_logDirectory);
    }

    /// <inheritdoc />
    public void Info(string message) => Write("INFO", message, exception: null);

    /// <inheritdoc />
    public void Warn(string message) => Write("WARN", message, exception: null);

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    /// <summary>Resolve the file path for a specific date - used by tests for assertions.</summary>
    public string GetLogFilePath(DateTimeOffset date)
        => Path.Combine(_logDirectory, $"launcher-{date.UtcDateTime:yyyy-MM-dd}.log");

    private void Write(string level, string message, Exception? exception)
    {
        var now = _clock();
        var path = GetLogFilePath(now);

        var sb = new StringBuilder(capacity: message.Length + 64);
        sb.Append(now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        sb.Append(" [").Append(level).Append("] ").Append(message ?? string.Empty).AppendLine();
        if (exception is not null)
        {
            sb.Append("    at ").Append(exception.ToString()).AppendLine();
        }

        lock (_sync)
        {
            Directory.CreateDirectory(_logDirectory);
            File.AppendAllText(path, sb.ToString(), LogEncoding);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No persistent handles - reserved for symmetry / future buffering.
    }
}
