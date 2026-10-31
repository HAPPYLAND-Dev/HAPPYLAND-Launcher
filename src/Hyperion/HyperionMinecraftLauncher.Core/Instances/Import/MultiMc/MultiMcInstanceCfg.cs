using System;
using System.Globalization;
using System.IO;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Instances.Import.MultiMc;

/// <summary>
/// Parsed snapshot of MultiMC / Prism's <c>instance.cfg</c> - an INI-ish flat key/value file
/// (no sections in the Prism dialect, despite the historical <c>[General]</c> header in old
/// MultiMC builds). Every field is nullable; the caller decides what to do with missing keys.
/// </summary>
/// <remarks>
/// Only the handful of keys Hyperion can actually round-trip are extracted. The full Prism
/// schema is much larger (JavaPath, JavaVersion, MCLaunchMethod, JoinServerOnLaunch, ...);
/// the rest is forward-compat surface we silently drop on import.
/// </remarks>
public sealed record MultiMcInstanceCfg
{
    /// <summary>The user-facing name from the <c>name=</c> key. Null when missing.</summary>
    public string? Name { get; init; }

    /// <summary>Icon key (e.g. <c>flame</c>, <c>grass</c>). Null when missing.</summary>
    public string? IconKey { get; init; }

    /// <summary>Extra JVM args appended after the launcher's defaults. Null when missing or empty.</summary>
    public string? JvmArgs { get; init; }

    /// <summary>Minimum heap in MB (Prism's <c>MinMemAlloc</c>). Null when missing or unparsable.</summary>
    public int? MinMemAllocMb { get; init; }

    /// <summary>Maximum heap in MB (Prism's <c>MaxMemAlloc</c>). Null when missing or unparsable.</summary>
    public int? MaxMemAllocMb { get; init; }

    /// <summary>Free-text user notes. Null when missing.</summary>
    public string? Notes { get; init; }
}

/// <summary>
/// Tolerant parser for MultiMC / Prism <c>instance.cfg</c> files. Accepts <c>key=value</c> and
/// <c>key:value</c> separators (Prism uses <c>=</c>, but at least one community variant uses
/// <c>:</c>), ignores <c>#</c> and <c>;</c> line comments, strips matching surrounding quotes,
/// and skips bare section headers (<c>[General]</c>) so we don't choke on old MultiMC files.
/// </summary>
public static class MultiMcInstanceCfgParser
{
    /// <summary>Parse a raw cfg string. Empty input returns a record with all fields null.</summary>
    public static MultiMcInstanceCfg Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        string? name = null;
        string? icon = null;
        string? jvm = null;
        int? minMem = null;
        int? maxMem = null;
        string? notes = null;

        using var reader = new StringReader(raw);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            // Comments: # or ; at line start.
            if (trimmed[0] == '#' || trimmed[0] == ';') continue;

            // Section headers ([General], [Java], ...): MultiMC <-> Prism varies; ignore them.
            if (trimmed[0] == '[' && trimmed.EndsWith(']')) continue;

            // Find separator. Prefer the first '='; fall back to ':' so users who hand-edited
            // YAML-style files still parse. We deliberately don't try to be clever about quoted
            // separators - cfg values rarely contain unescaped '=' / ':' anyway.
            var sepIndex = trimmed.IndexOf('=');
            if (sepIndex < 0) sepIndex = trimmed.IndexOf(':');
            if (sepIndex <= 0) continue;

            var key = trimmed.Substring(0, sepIndex).Trim();
            var value = trimmed.Substring(sepIndex + 1).Trim();

            // Strip surrounding double or single quotes (Prism emits unquoted, but we accept both).
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value.Substring(1, value.Length - 2);
            }

            switch (key)
            {
                case "name":
                    name = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
                case "iconKey":
                    icon = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
                case "JvmArgs":
                    jvm = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
                case "MinMemAlloc":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var min))
                        minMem = min;
                    break;
                case "MaxMemAlloc":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max))
                        maxMem = max;
                    break;
                case "notes":
                    notes = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
                // Everything else (JavaPath, JavaVersion, JoinServerOnLaunch, ...) is ignored.
            }
        }

        return new MultiMcInstanceCfg
        {
            Name = name,
            IconKey = icon,
            JvmArgs = jvm,
            MinMemAllocMb = minMem,
            MaxMemAllocMb = maxMem,
            Notes = notes,
        };
    }
}
