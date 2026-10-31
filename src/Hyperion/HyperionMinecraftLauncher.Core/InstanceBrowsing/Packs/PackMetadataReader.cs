using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.InstanceBrowsing.Packs;

/// <summary>Which folder a pack came from; shader packs have no <c>pack.mcmeta</c> / <c>pack.png</c> convention.</summary>
public enum PackKind
{
    Resource,
    Data,
    Shader,
}

/// <summary>What the game's pack screen shows for a pack: its icon and its formatted description.</summary>
public sealed record PackMetadata
{
    public static readonly PackMetadata Empty = new();

    /// <summary>PNG bytes of the pack icon, or null when the pack has none.</summary>
    public byte[]? IconPng { get; init; }

    /// <summary>Formatted <c>pack.mcmeta</c> description; empty when absent.</summary>
    public IReadOnlyList<McTextSpan> Description { get; init; } = Array.Empty<McTextSpan>();

    /// <summary><c>pack.pack_format</c>, when declared.</summary>
    public int? PackFormat { get; init; }
}

/// <summary>
/// Reads icon and description out of a zipped pack. Resource and data packs follow the game's
/// convention (<c>pack.png</c>, <c>pack.mcmeta</c> at the root). Shader packs have none, so the
/// first image from the usual spots is used: the root, <c>shaders/</c>, or the same two under a
/// single top folder (packs zipped together with their folder). Never throws: an unreadable
/// pack simply gets <see cref="PackMetadata.Empty"/>.
/// </summary>
public static class PackMetadataReader
{
    /// <summary>Icons above this size are skipped (a pack icon is normally a few KB).</summary>
    public const int MaxIconBytes = 2 * 1024 * 1024;

    private static readonly string[] ShaderIconNames = { "pack.png", "logo.png", "icon.png" };

    public static PackMetadata Read(string zipPath, PackKind kind)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var byName = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in zip.Entries)
                byName.TryAdd(e.FullName.Replace('\\', '/'), e);

            return kind == PackKind.Shader
                ? new PackMetadata { IconPng = ReadIcon(FindShaderIcon(byName)) }
                : new PackMetadata
                {
                    IconPng = ReadIcon(byName.GetValueOrDefault("pack.png")),
                    Description = ReadMcmeta(byName.GetValueOrDefault("pack.mcmeta"), out var format),
                    PackFormat = format,
                };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return PackMetadata.Empty;
        }
    }

    private static ZipArchiveEntry? FindShaderIcon(Dictionary<string, ZipArchiveEntry> byName)
    {
        var roots = new List<string> { string.Empty };
        // One top-level folder holding everything, e.g. "BSL_v8.4/shaders/...".
        var tops = byName.Keys.Where(k => k.Contains('/')).Select(k => k[..(k.IndexOf('/') + 1)]).Distinct().ToList();
        if (tops.Count == 1 && tops[0] != "shaders/") roots.Add(tops[0]);

        foreach (var root in roots)
            foreach (var dir in new[] { string.Empty, "shaders/" })
                foreach (var name in ShaderIconNames)
                    if (byName.TryGetValue(root + dir + name, out var entry))
                        return entry;
        return null;
    }

    private static byte[]? ReadIcon(ZipArchiveEntry? entry)
    {
        if (entry is null || entry.Length is 0 or > MaxIconBytes) return null;
        using var s = entry.Open();
        using var ms = new MemoryStream((int)entry.Length);
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static IReadOnlyList<McTextSpan> ReadMcmeta(ZipArchiveEntry? entry, out int? packFormat)
    {
        packFormat = null;
        if (entry is null) return Array.Empty<McTextSpan>();
        try
        {
            using var s = entry.Open();
            // JsonDocument skips a UTF-8 BOM itself; some packs also carry trailing commas.
            using var doc = JsonDocument.Parse(s, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (!doc.RootElement.TryGetProperty("pack", out var pack) || pack.ValueKind != JsonValueKind.Object)
                return Array.Empty<McTextSpan>();
            if (pack.TryGetProperty("pack_format", out var f) && f.TryGetInt32(out var n))
                packFormat = n;
            return pack.TryGetProperty("description", out var d) ? McText.FromJson(d) : Array.Empty<McTextSpan>();
        }
        catch (JsonException)
        {
            return Array.Empty<McTextSpan>();
        }
    }
}
