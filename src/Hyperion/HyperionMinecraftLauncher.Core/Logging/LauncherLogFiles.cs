using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

/// <summary>
/// Locates the files <see cref="SerilogLauncherLogger"/> writes. Serilog's day roll stamps
/// the <c>launcher-.log</c> template with the local date as <c>yyyyMMdd</c>
/// (<c>launcher-20260922.log</c>), and a size roll adds <c>_001</c>, <c>_002</c>, ... on top.
/// </summary>
public static class LauncherLogFiles
{
    private const string Prefix = "launcher-";
    private const string Extension = ".log";

    /// <summary>Path of the first file Serilog opens for <paramref name="localDate"/>.</summary>
    public static string ExpectedPath(string directory, DateTime localDate)
        => Path.Combine(directory, Prefix + Stamp(localDate) + Extension);

    /// <summary>
    /// Newest file written for <paramref name="localDate"/> (the highest size-roll part), or
    /// <c>null</c> when the directory or the day's file does not exist.
    /// </summary>
    public static string? FindForDate(string directory, DateTime localDate)
    {
        if (!Directory.Exists(directory)) return null;

        var stamp = Stamp(localDate);
        // Ordinal order puts "launcher-20260922.log" ('.') before "_001" ('_'), and the
        // zero-padded part numbers sort naturally after it.
        return Directory.EnumerateFiles(directory, Prefix + stamp + "*" + Extension)
            .Where(p => IsPartOf(Path.GetFileName(p), stamp))
            .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
            .LastOrDefault();
    }

    private static bool IsPartOf(string fileName, string stamp)
    {
        var rest = fileName.Substring(Prefix.Length + stamp.Length);
        return rest == Extension
            || (rest.Length > Extension.Length + 1 && rest[0] == '_'
                && rest.EndsWith(Extension, StringComparison.Ordinal)
                && rest[1..^Extension.Length].All(char.IsAsciiDigit));
    }

    private static string Stamp(DateTime localDate)
        => localDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
}
