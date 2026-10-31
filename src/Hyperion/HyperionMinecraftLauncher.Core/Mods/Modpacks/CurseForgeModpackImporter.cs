using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modpacks;

/// <summary>
/// Imports a CurseForge modpack zip into a fresh Hyperion <see cref="Instance"/>.
///
/// The CurseForge format is a zip with:
/// <list type="bullet">
///   <item>A top-level <c>manifest.json</c> (minecraft.version, minecraft.modLoaders[], files[]).</item>
///   <item>Per-file <c>projectID</c> / <c>fileID</c> pairs that the launcher must resolve to a
///         download URL via the CurseForge v1 API (no direct URLs in the manifest).</item>
///   <item>An optional <c>overrides/</c> tree (configurable via the <c>overrides</c> manifest key).</item>
/// </list>
///
/// Construction takes the per-instance root directory + an injected <see cref="IModRepository"/>
/// (typically the live <c>CurseForgeRepository</c>). The repository resolves <c>fileID</c>
/// into a real <see cref="ModFile.DownloadUrl"/> via <see cref="IModRepository.ListFilesAsync"/>
/// and then streams the bytes back through <see cref="IModRepository.DownloadAsync"/>.
/// </summary>
public sealed class CurseForgeModpackImporter : IModpackImporter
{
    private const string ManifestEntry = ModpackFormatDetector.CurseForgeManifestEntry;
    private const string DefaultOverridesPath = "overrides";

    private readonly string _instancesRoot;
    private readonly IModRepository _curseForgeRepository;

    /// <summary>
    /// Construct against a per-instance root directory + the live CurseForge repository.
    /// The constructor only validates arguments; the directory is created lazily on import.
    /// </summary>
    public CurseForgeModpackImporter(string instancesRoot, IModRepository curseForgeRepository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instancesRoot);
        _instancesRoot = instancesRoot;
        _curseForgeRepository = curseForgeRepository ?? throw new ArgumentNullException(nameof(curseForgeRepository));
    }

    /// <inheritdoc />
    public async Task<Instance> ImportAsync(
        string archivePath,
        string? targetInstanceName,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Modpack archive not found.", archivePath);

        ModpackManifest manifest;
        IReadOnlyList<CurseForgeFileRef> fileRefs;
        await using (var fs = File.OpenRead(archivePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
        {
            (manifest, fileRefs) = ParseManifest(zip);
        }

        var instanceId = Guid.NewGuid().ToString("N");
        var instanceDir = Path.Combine(_instancesRoot, instanceId);
        Directory.CreateDirectory(instanceDir);
        var modsDir = Path.Combine(instanceDir, "mods");
        Directory.CreateDirectory(modsDir);

        var name = string.IsNullOrWhiteSpace(targetInstanceName) ? manifest.Name : targetInstanceName;

        // 1) For each declared file: resolve via the CurseForge API, then stream the bytes
        //    into mods/<filename>. We do these one at a time rather than in parallel to
        //    keep the API call rate at the same level the rest of the launcher uses.
        var total = Math.Max(1, fileRefs.Count);
        var completed = 0;
        foreach (var fileRef in fileRefs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!fileRef.Required)
            {
                completed++;
                continue;
            }

            var files = await _curseForgeRepository.ListFilesAsync(
                fileRef.ProjectId.ToString(CultureInfo.InvariantCulture),
                gameVersion: null,
                loader: null,
                cancellationToken).ConfigureAwait(false);

            ModFile? match = null;
            foreach (var f in files)
            {
                if (string.Equals(f.FileId, fileRef.FileId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                {
                    match = f;
                    break;
                }
            }
            if (match is null)
                throw new InvalidDataException(
                    $"CurseForge file {fileRef.FileId} not found under project {fileRef.ProjectId} - " +
                    "the API returned no matching file. Pack may reference a removed version.");

            var filename = string.IsNullOrWhiteSpace(match.Filename)
                ? DeriveFilenameFromUrl(match.DownloadUrl)
                : match.Filename!;
            var dest = Path.Combine(modsDir, filename);

            await using (var output = File.Create(dest))
            {
                await _curseForgeRepository.DownloadAsync(match, output, progress: null, cancellationToken).ConfigureAwait(false);
            }
            completed++;
            progress?.Report((double)completed / total);
        }

        // 2) Copy overrides on top.
        if (!string.IsNullOrEmpty(manifest.OverridesPath))
        {
            await using var fs = File.OpenRead(archivePath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            CopyOverrides(zip, manifest.OverridesPath!, instanceDir, cancellationToken);
        }

        progress?.Report(1.0);

        return new Instance
        {
            Id = instanceId,
            Name = name,
            VersionId = manifest.MinecraftVersion,
            Loader = manifest.Loader,
            LoaderVersion = manifest.LoaderVersion,
            GameDirectory = instanceDir,
            IconKey = InstanceIcons.Chest,
        };
    }

    /// <summary>
    /// Parse <c>manifest.json</c> out of a CurseForge modpack archive. Public so tests
    /// can stuff a synthetic zip through without disk I/O. Returns the format-agnostic
    /// manifest plus the CurseForge-specific (projectID, fileID) pairs the importer
    /// resolves via the live API.
    /// </summary>
    public static (ModpackManifest Manifest, IReadOnlyList<CurseForgeFileRef> Files) ParseManifest(ZipArchive zip)
    {
        ArgumentNullException.ThrowIfNull(zip);
        var entry = zip.GetEntry(ManifestEntry)
            ?? throw new InvalidDataException($"Missing '{ManifestEntry}' inside the CurseForge modpack archive.");

        using var stream = entry.Open();
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var name = ReadString(root, "name") ?? "Modpack";
        var version = ReadString(root, "version") ?? string.Empty;

        var mcVersion = string.Empty;
        var loader = ModLoader.None;
        string? loaderVersion = null;
        if (root.TryGetProperty("minecraft", out var minecraft) && minecraft.ValueKind == JsonValueKind.Object)
        {
            mcVersion = ReadString(minecraft, "version") ?? string.Empty;

            if (minecraft.TryGetProperty("modLoaders", out var loaders) && loaders.ValueKind == JsonValueKind.Array)
            {
                JsonElement primary = default;
                var foundPrimary = false;
                JsonElement firstAny = default;
                var foundAny = false;
                foreach (var l in loaders.EnumerateArray())
                {
                    if (!foundAny) { firstAny = l; foundAny = true; }
                    if (l.TryGetProperty("primary", out var prim) && prim.ValueKind == JsonValueKind.True)
                    {
                        primary = l;
                        foundPrimary = true;
                        break;
                    }
                }
                var chosen = foundPrimary ? primary : (foundAny ? firstAny : default);
                if (foundPrimary || foundAny)
                {
                    var id = ReadString(chosen, "id") ?? string.Empty;
                    (loader, loaderVersion) = ParseLoaderId(id);
                }
            }
        }

        // overrides path: top-level manifest key, default "overrides".
        var overridesPath = ReadString(root, "overrides") ?? DefaultOverridesPath;
        // Confirm the folder exists inside the zip - otherwise null it out so we don't try to copy nothing.
        var prefix = overridesPath.EndsWith('/') ? overridesPath : overridesPath + "/";
        var overridesPresent = false;
        foreach (var z in zip.Entries)
        {
            if (z.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                overridesPresent = true;
                break;
            }
        }

        var files = ReadFiles(root);

        return (new ModpackManifest
        {
            Name = name,
            Version = version,
            MinecraftVersion = mcVersion,
            Loader = loader,
            LoaderVersion = loaderVersion,
            // The format-agnostic Files list is empty for CurseForge: we resolve URLs at import time.
            Files = Array.Empty<ModpackFile>(),
            OverridesPath = overridesPresent ? overridesPath : null,
        }, files);
    }

    private static IReadOnlyList<CurseForgeFileRef> ReadFiles(JsonElement root)
    {
        if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            return Array.Empty<CurseForgeFileRef>();

        var list = new List<CurseForgeFileRef>(files.GetArrayLength());
        foreach (var f in files.EnumerateArray())
        {
            var projectId = ReadInt64(f, "projectID");
            var fileId = ReadInt64(f, "fileID");
            if (projectId <= 0 || fileId <= 0) continue;

            var required = true;
            if (f.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.False)
                required = false;

            list.Add(new CurseForgeFileRef(projectId, fileId, required));
        }
        return list;
    }

    /// <summary>
    /// Parse a CurseForge loader id like <c>"forge-47.2.0"</c> / <c>"fabric-0.14.21"</c>.
    /// Returns (None, null) when the format isn't recognised - the launcher still imports
    /// the pack, the user just has to install the loader themselves.
    /// </summary>
    public static (ModLoader Loader, string? LoaderVersion) ParseLoaderId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return (ModLoader.None, null);
        var dash = id.IndexOf('-');
        var prefix = dash > 0 ? id[..dash] : id;
        var suffix = dash > 0 && dash < id.Length - 1 ? id[(dash + 1)..] : null;
        return prefix.ToLowerInvariant() switch
        {
            "forge" => (ModLoader.Forge, suffix),
            "fabric" => (ModLoader.Fabric, suffix),
            "neoforge" => (ModLoader.NeoForge, suffix),
            "quilt" => (ModLoader.Quilt, suffix),
            _ => (ModLoader.None, null),
        };
    }

    private static void CopyOverrides(ZipArchive zip, string overridesRoot, string instanceDir, CancellationToken cancellationToken)
    {
        var prefix = overridesRoot.EndsWith('/') ? overridesRoot : overridesRoot + "/";
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = entry.FullName.Substring(prefix.Length);
            if (string.IsNullOrEmpty(relative)) continue;
            var dest = Path.Combine(instanceDir, relative.Replace('/', Path.DirectorySeparatorChar));

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(dest);
                continue;
            }

            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
            using var src = entry.Open();
            using var fs = File.Create(dest);
            src.CopyTo(fs);
        }
    }

    private static string DeriveFilenameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var last = uri.Segments.Length > 0 ? Uri.UnescapeDataString(uri.Segments[^1]) : null;
            if (!string.IsNullOrWhiteSpace(last)) return last;
        }
        catch { /* fall through */ }
        return "mod.jar";
    }

    private static string? ReadString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static long ReadInt64(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt64(out var n) ? n : (long)v.GetDouble(),
            _ => 0,
        };
    }
}

/// <summary>One (projectID, fileID, required) tuple from a CurseForge modpack manifest.</summary>
public readonly record struct CurseForgeFileRef(long ProjectId, long FileId, bool Required);
