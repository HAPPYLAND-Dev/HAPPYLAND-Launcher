using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.CrashReports;

/// <summary>
/// Default <see cref="ICrashReportParser"/>. Recognises the three layouts vanilla / Forge / Fabric
/// each produce. The strategy is:
/// <list type="number">
///   <item>Parse the well-known header lines for Minecraft + loader version.</item>
///   <item>Capture the "Mod List" / "Mods loaded" block (Forge format vs Fabric format differs); use it as a friendly lookup table from package-prefix to display name.</item>
///   <item>Walk every <c>at &lt;package&gt;.&lt;Class&gt;(...)</c> line in stack traces; suspect any frame whose package prefix is not in the well-known "ignore" set (Minecraft, Java stdlib, the loaders themselves, common libraries).</item>
/// </list>
/// The result is a CrashReport whose <see cref="CrashReport.SuspectedMods"/> ranks the first
/// suspect frame seen first (closest to the actual exception, which is what Prism uses too).
/// </summary>
public sealed class MinecraftCrashReportParser : ICrashReportParser
{
    // Package roots we never suspect - they're the engine, the loaders, the JDK, or known libs.
    // Anything else hit by the stack trace is a candidate mod.
    private static readonly string[] IgnoredPackageRoots = new[]
    {
        "java.",
        "javax.",
        "jdk.",
        "sun.",
        "com.sun.",
        "kotlin.",
        "kotlinx.",
        "scala.",
        "org.lwjgl.",
        "org.slf4j.",
        "org.apache.logging.",
        "org.apache.log4j.",
        "org.objectweb.asm.",
        "org.spongepowered.asm.",
        // Minecraft itself.
        "net.minecraft.",
        "com.mojang.",
        // The loaders.
        "net.minecraftforge.",
        "cpw.mods.",
        "net.fabricmc.",
        "org.quiltmc.",
        "net.neoforged.",
    };

    // crash-2024-08-12_14.05.33-client.txt
    private static readonly Regex FilenameTimestamp = new(
        @"^crash-(?<y>\d{4})-(?<mo>\d{2})-(?<d>\d{2})_(?<h>\d{2})\.(?<mi>\d{2})\.(?<s>\d{2})-",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MinecraftVersionLine = new(
        @"^\s*Minecraft Version\s*:\s*(?<v>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private static readonly Regex ForgeVersionLine = new(
        @"^\s*Forge\s+Version\s*:\s*(?<v>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    // Fabric mostly identifies via a "Fabric Loader: 0.16.5" or "Fabric Loader version: 0.16.5" entry.
    private static readonly Regex FabricVersionLine = new(
        @"^\s*Fabric Loader(?:\s+version)?\s*:\s*(?<v>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    // Stack frame: "at net.foo.Bar$Inner.method(Bar.java:42) ~[bar-1.0.jar:?]".
    // Captures the fully-qualified type name, which is everything up to '(' minus the trailing ".method".
    private static readonly Regex StackFrame = new(
        @"^\s*at\s+(?<fqcn>[a-zA-Z_$][\w$]*(?:\.[a-zA-Z_$][\w$]*)+)\.[a-zA-Z_$<][\w$<>\$]*\s*\(",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Forge mod list rows: "        sodium                       | Sodium                         | sodium-fabric-0.5.8 | DONE   | Manifest: NOSIGNATURE"
    // Loose pipe-separated row: we only need the first two columns.
    private static readonly Regex ForgeModRow = new(
        @"^\s*(?<id>[a-z0-9_][a-z0-9_\-]*)\s*\|\s*(?<name>[^|]+?)\s*\|",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    // Fabric mod list rows are indented hyphens: "    - sodium 0.5.8" or "  - sodium-extra 5.2.3".
    private static readonly Regex FabricModRow = new(
        @"^\s*-\s+(?<id>[a-z0-9_][a-z0-9_\-]*)\s+(?<ver>[\w\.\-\+]+)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <inheritdoc />
    public Task<CrashReport> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Path cannot be empty", nameof(filePath));

        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            return Parse(filePath, text);
        }, cancellationToken);
    }

    /// <summary>Test-friendly sync entry point: parse a string that already lives in memory.</summary>
    public CrashReport Parse(string filePath, string text)
    {
        var filename = Path.GetFileName(filePath);
        var generatedAt = TryParseTimestampFromFilename(filename)
            ?? TryReadMtime(filePath)
            ?? DateTimeOffset.UtcNow;

        var minecraftVersion = MatchOrEmpty(MinecraftVersionLine, text, "v");
        var loader = TryReadLoaderLabel(text);

        var summary = ExtractSummaryLine(text);
        var modNameLookup = BuildModNameLookup(text);
        var suspects = ExtractSuspectFrames(text, modNameLookup);

        return new CrashReport
        {
            FilePath = filePath,
            Filename = filename,
            GeneratedAt = generatedAt,
            MinecraftVersion = minecraftVersion,
            ForgeOrFabricVersion = loader,
            SummaryLine = summary,
            SuspectedMods = suspects,
            FullText = text,
        };
    }

    // ----- timestamp helpers -----

    private static DateTimeOffset? TryParseTimestampFromFilename(string filename)
    {
        var m = FilenameTimestamp.Match(filename);
        if (!m.Success) return null;
        try
        {
            var dt = new DateTime(
                int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups["mo"].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups["mi"].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture),
                DateTimeKind.Local);
            return new DateTimeOffset(dt);
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? TryReadMtime(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            return new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero);
        }
        catch
        {
            return null;
        }
    }

    // ----- header helpers -----

    private static string MatchOrEmpty(Regex r, string text, string group)
    {
        var m = r.Match(text);
        return m.Success ? m.Groups[group].Value.Trim() : string.Empty;
    }

    private static string? TryReadLoaderLabel(string text)
    {
        var forge = ForgeVersionLine.Match(text);
        if (forge.Success) return "Forge " + forge.Groups["v"].Value.Trim();
        var fab = FabricVersionLine.Match(text);
        if (fab.Success) return "Fabric " + fab.Groups["v"].Value.Trim();
        return null;
    }

    private static string ExtractSummaryLine(string text)
    {
        // Forge / vanilla pattern:
        //   ---- Minecraft Crash Report ----
        //   // joke or description here
        //   <blank>
        //   Time: ...
        // We grab the first non-blank line after the header banner.
        var headerIdx = text.IndexOf("---- Minecraft Crash Report ----", StringComparison.OrdinalIgnoreCase);
        if (headerIdx < 0)
        {
            // No banner - just return the first 200 chars of the first non-empty line.
            using var rdr = new StringReader(text);
            string? line;
            while ((line = rdr.ReadLine()) is not null)
            {
                if (!string.IsNullOrWhiteSpace(line)) return Trim(line);
            }
            return string.Empty;
        }

        // Skip past the banner line.
        var rest = text.Substring(headerIdx);
        using var reader = new StringReader(rest);
        reader.ReadLine(); // discard banner
        string? candidate;
        while ((candidate = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            return Trim(candidate);
        }
        return string.Empty;

        static string Trim(string s)
        {
            s = s.Trim();
            if (s.Length > 200) s = s.Substring(0, 200);
            return s;
        }
    }

    // ----- mod list parsing -----

    private static Dictionary<string, string> BuildModNameLookup(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Forge style: pipe-separated table after a "Mod List:" header.
        foreach (Match m in ForgeModRow.Matches(text))
        {
            var id = m.Groups["id"].Value.Trim();
            var name = m.Groups["name"].Value.Trim();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) continue;
            if (string.Equals(name, "Name", StringComparison.OrdinalIgnoreCase)) continue; // header row
            if (!map.ContainsKey(id)) map[id] = name;
        }

        // Fabric style: hyphenated list after "Fabric Mods:".
        var fabricBlockStart = text.IndexOf("Fabric Mods:", StringComparison.OrdinalIgnoreCase);
        if (fabricBlockStart >= 0)
        {
            var rest = text.Substring(fabricBlockStart);
            foreach (Match m in FabricModRow.Matches(rest))
            {
                var id = m.Groups["id"].Value.Trim();
                if (string.IsNullOrEmpty(id)) continue;
                // Fabric only gives us an id, no friendly display name - mirror id.
                if (!map.ContainsKey(id)) map[id] = id;
            }
        }

        return map;
    }

    // ----- stack-frame suspect extraction -----

    private List<CrashFrame> ExtractSuspectFrames(string text, IReadOnlyDictionary<string, string> modNameLookup)
    {
        var result = new List<CrashFrame>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in StackFrame.Matches(text))
        {
            var fqcn = m.Groups["fqcn"].Value;
            if (IsIgnoredPackage(fqcn)) continue;

            var modId = InferModId(fqcn, modNameLookup);
            if (modId is null) continue;
            if (!seenIds.Add(modId)) continue;

            var displayName = modNameLookup.TryGetValue(modId, out var name) ? name : modId;
            var frameLine = m.Value.TrimEnd('\r', '\n');

            result.Add(new CrashFrame
            {
                FrameLine = frameLine.Trim(),
                ModId = modId,
                ModDisplayName = displayName,
                ModrinthSearchUrl = $"https://modrinth.com/mod?q={Uri.EscapeDataString(modId)}",
                CurseForgeSearchUrl = $"https://www.curseforge.com/minecraft/mc-mods/search?search={Uri.EscapeDataString(modId)}",
            });

            // Cap: 10 suspects is more than the UI can usefully show without becoming noise.
            if (result.Count >= 10) break;
        }

        return result;
    }

    private static bool IsIgnoredPackage(string fqcn)
    {
        foreach (var root in IgnoredPackageRoots)
        {
            if (fqcn.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Walk the package segments left-to-right, preferring the longest match that exists
    /// in <paramref name="modNameLookup"/>. Falls back to the very first segment so we
    /// always get something useful to search for (e.g. "sodium" out of "sodium.client.Foo").
    /// </summary>
    private static string? InferModId(string fqcn, IReadOnlyDictionary<string, string> modNameLookup)
    {
        var parts = fqcn.Split('.');
        if (parts.Length == 0) return null;

        // Try descending prefix match against the loaded-mods list, longest first.
        for (var take = Math.Min(parts.Length - 1, 3); take >= 1; take--)
        {
            var prefix = string.Join('.', parts.Take(take));
            // Strip the "com.", "io." style two-letter group prefix - mod ids almost never
            // include the reverse-DNS group. e.g. com.sodium.client -> sodium.
            var candidate = StripDnsPrefix(prefix);
            if (modNameLookup.ContainsKey(candidate)) return candidate;
            // Try the raw prefix too in case the table uses "io.something".
            if (modNameLookup.ContainsKey(prefix)) return prefix;
        }

        // No explicit match; synthesize a plausible mod id from the leading segments.
        var top = StripDnsPrefix(parts[0]);
        // If "com" / "io" / "me" / "net" / "org" was the leading group, prefer the second segment as the mod id.
        if (parts.Length >= 2 && IsDnsGroup(parts[0]))
            top = parts[1];
        // Last fallback - the very first segment.
        return string.IsNullOrEmpty(top) ? null : top.ToLowerInvariant();
    }

    private static string StripDnsPrefix(string s)
    {
        var parts = s.Split('.');
        if (parts.Length >= 2 && IsDnsGroup(parts[0]))
            return string.Join('.', parts.Skip(1));
        return s;
    }

    private static bool IsDnsGroup(string s)
    {
        return s switch
        {
            "com" or "io" or "me" or "net" or "org" or "dev" or "co" or "app" or "info" => true,
            _ => false,
        };
    }
}
