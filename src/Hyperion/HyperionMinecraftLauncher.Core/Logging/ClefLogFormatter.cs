using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

/// <summary>
/// Turns the compact-JSON (CLEF) lines <see cref="SerilogLauncherLogger"/> writes into
/// human-readable text for the Logs page: <c>HH:mm:ss LVL message</c>, with any exception
/// indented on the following lines. Lines that are not CLEF events pass through unchanged.
/// </summary>
public static class ClefLogFormatter
{
    private const string ExceptionIndent = "    ";

    /// <summary>Format a whole log file. Blank lines are dropped.</summary>
    public static string FormatText(string text, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0)
            .Select(l => FormatLine(l, zone));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Format one CLEF event. Anything that is not a CLEF object is returned as-is.</summary>
    public static string FormatLine(string line, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (string.IsNullOrEmpty(line) || line[0] != '{') return line;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("@t", out var t)
                || !DateTimeOffset.TryParse(t.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
                return line;

            var level = root.TryGetProperty("@l", out var l) ? l.GetString() : null;
            var message = ReadString(root, "Message") ?? ReadString(root, "@m") ?? ReadString(root, "@mt") ?? string.Empty;
            var local = TimeZoneInfo.ConvertTime(timestamp, zone);
            var formatted = $"{local.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} {Abbreviate(level)} {message}";

            if (ReadString(root, "@x") is { Length: > 0 } exception)
            {
                var indented = exception.Replace("\r\n", "\n").Split('\n')
                    .Select(x => ExceptionIndent + x);
                formatted += Environment.NewLine + string.Join(Environment.NewLine, indented);
            }
            return formatted;
        }
        catch (JsonException)
        {
            return line;
        }
    }

    private static string Abbreviate(string? level) => level switch
    {
        null or "" or "Information" => "INF",
        "Warning" => "WRN",
        "Error" => "ERR",
        "Fatal" => "FTL",
        "Debug" => "DBG",
        "Verbose" => "VRB",
        _ => level.ToUpperInvariant(),
    };

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
