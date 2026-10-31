using System;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

/// <summary>
/// Pure-function helper that filters multi-line log text down to lines that contain
/// a case-insensitive substring. Extracted so the UI's Logs page filter behaviour can
/// be unit-tested without spinning up an Avalonia view-model.
/// </summary>
public static class LogFiltering
{
    /// <summary>
    /// Returns <paramref name="text"/> filtered to only the lines that contain
    /// <paramref name="filter"/> (case-insensitive substring match), joined with
    /// <see cref="Environment.NewLine"/>.
    /// </summary>
    /// <remarks>
    /// - A null or empty <paramref name="filter"/> returns the input unchanged so the
    ///   caller can wire a single binding that "shows everything when the search box is empty".
    /// - Line order is preserved.
    /// - Both <c>\r\n</c> and bare <c>\n</c> line endings in the input are accepted;
    ///   the output uses <see cref="Environment.NewLine"/> between kept lines.
    /// - A null <paramref name="text"/> is treated as empty so consumers don't have to null-guard.
    /// </remarks>
    public static string Filter(string text, string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return text ?? string.Empty;

        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Split on either '\n' or '\r\n' so we don't lose lines when the file was written
        // on a different platform than the one reading it.
        var lines = text.Split('\n');
        var matched = new System.Text.StringBuilder(capacity: text.Length);
        var firstAppended = true;

        foreach (var raw in lines)
        {
            // Strip trailing '\r' from the \r\n case so substring matches are predictable.
            var line = raw.Length > 0 && raw[^1] == '\r' ? raw.Substring(0, raw.Length - 1) : raw;
            if (line.Length == 0) continue;

            if (line.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (firstAppended)
                {
                    matched.Append(line);
                    firstAppended = false;
                }
                else
                {
                    matched.Append(Environment.NewLine).Append(line);
                }
            }
        }

        return matched.ToString();
    }
}
