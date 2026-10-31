using System;
using System.IO;
using System.IO.Compression;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modpacks;

/// <summary>The on-disk format of a modpack archive.</summary>
public enum ModpackFormat
{
    /// <summary>Archive doesn't contain a recognised manifest at its root.</summary>
    Unknown,

    /// <summary>Modrinth <c>.mrpack</c> (zip with a top-level <c>modrinth.index.json</c>).</summary>
    Modrinth,

    /// <summary>CurseForge zip (top-level <c>manifest.json</c> with a CurseForge schema).</summary>
    CurseForge,
}

/// <summary>Helpers for figuring out which modpack format an archive uses.</summary>
public static class ModpackFormatDetector
{
    /// <summary>The Modrinth manifest filename, expected at the archive root.</summary>
    public const string ModrinthManifestEntry = "modrinth.index.json";

    /// <summary>The CurseForge manifest filename, expected at the archive root.</summary>
    public const string CurseForgeManifestEntry = "manifest.json";

    /// <summary>
    /// Inspect the archive at <paramref name="archivePath"/> and return the format we
    /// recognise. If both manifests are present (unusual but legal), Modrinth wins.
    /// </summary>
    public static ModpackFormat Detect(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        using var fs = File.OpenRead(archivePath);
        return Detect(fs);
    }

    /// <summary>
    /// Inspect an open stream containing a modpack archive and return the format we
    /// recognise. The stream is read in zip mode; the caller still owns disposal.
    /// </summary>
    public static ModpackFormat Detect(Stream archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        var hasModrinth = false;
        var hasCurseForge = false;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName;
            if (string.Equals(name, ModrinthManifestEntry, StringComparison.OrdinalIgnoreCase))
                hasModrinth = true;
            else if (string.Equals(name, CurseForgeManifestEntry, StringComparison.OrdinalIgnoreCase))
                hasCurseForge = true;
        }
        if (hasModrinth) return ModpackFormat.Modrinth;
        if (hasCurseForge) return ModpackFormat.CurseForge;
        return ModpackFormat.Unknown;
    }
}
