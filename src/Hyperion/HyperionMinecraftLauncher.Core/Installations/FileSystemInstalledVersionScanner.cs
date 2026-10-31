using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;

/// <summary>
/// <see cref="IInstalledVersionScanner"/> implementation that reads
/// <c>versions/&lt;id&gt;/&lt;id&gt;.json</c> directly from disk via <see cref="JsonDocument"/>.
/// No CmlLib dependency: kept portable across the Core's three TFMs.
/// </summary>
public sealed class FileSystemInstalledVersionScanner : IInstalledVersionScanner
{
    /// <inheritdoc />
    public IReadOnlyList<InstalledVersion> Scan(string versionsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionsDirectory);

        if (!Directory.Exists(versionsDirectory))
            return Array.Empty<InstalledVersion>();

        var list = new List<InstalledVersion>();
        foreach (var dir in Directory.EnumerateDirectories(versionsDirectory))
        {
            var id = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(id))
                continue;

            var jsonPath = Path.Combine(dir, $"{id}.json");
            if (!File.Exists(jsonPath))
                continue;

            if (TryParse(id, dir, jsonPath, out var installed))
                list.Add(installed);
        }

        list.Sort(CompareByReleaseTimeDescending);
        return list;
    }

    private static bool TryParse(string id, string dir, string jsonPath, out InstalledVersion result)
    {
        result = default!;
        try
        {
            using var stream = File.OpenRead(jsonPath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            var type = ReadString(root, "type") ?? "custom";
            var releaseTime = TryParseReleaseTime(root);
            var inheritsFrom = ReadString(root, "inheritsFrom");
            var mainClass = ReadString(root, "mainClass") ?? string.Empty;

            var jarPath = Path.Combine(dir, $"{id}.jar");
            result = new InstalledVersion
            {
                Id = id,
                Type = type,
                ReleaseTime = releaseTime,
                JsonPath = jsonPath,
                JarPath = File.Exists(jarPath) ? jarPath : null,
                Loader = LoaderDetector.Detect(id, mainClass, inheritsFrom),
                ParentVersionId = inheritsFrom,
            };
            return true;
        }
        catch
        {
            // Malformed manifest - skip silently. We do not log here because the scanner
            // is meant to be cheap and side-effect-free; the UI can show a "0 versions"
            // hint and the user can investigate via the log file if needed.
            return false;
        }
    }

    private static string? ReadString(JsonElement root, string property)
        => root.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static DateTimeOffset? TryParseReleaseTime(JsonElement root)
    {
        var s = ReadString(root, "releaseTime") ?? ReadString(root, "time");
        return DateTimeOffset.TryParse(s, out var dt) ? dt : null;
    }

    private static int CompareByReleaseTimeDescending(InstalledVersion a, InstalledVersion b)
    {
        // Versions with a known release time win - sort them newest-first. Versions without
        // one (mod loaders frequently omit it) fall back to id-descending so the user still
        // sees a stable, predictable order.
        if (a.ReleaseTime.HasValue && b.ReleaseTime.HasValue)
            return b.ReleaseTime.Value.CompareTo(a.ReleaseTime.Value);
        if (a.ReleaseTime.HasValue)
            return -1;
        if (b.ReleaseTime.HasValue)
            return 1;
        return string.Compare(b.Id, a.Id, StringComparison.OrdinalIgnoreCase);
    }
}
