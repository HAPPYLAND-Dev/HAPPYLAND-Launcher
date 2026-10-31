using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modpacks;

/// <summary>
/// Imports a Modrinth <c>.mrpack</c> archive into a fresh Hyperion <see cref="Instance"/>.
///
/// The <c>.mrpack</c> format is a zip with:
/// <list type="bullet">
///   <item>A top-level <c>modrinth.index.json</c> manifest (versionId, name, files[], dependencies).</item>
///   <item>An optional <c>overrides/</c> tree whose contents are copied verbatim into the instance dir.</item>
///   <item>An optional <c>client-overrides/</c> / <c>server-overrides/</c> tree (client wins for the launcher).</item>
/// </list>
///
/// Construction takes the per-instance root directory (e.g.
/// <c>%LOCALAPPDATA%/HyperionMinecraftLauncher/instances/</c>) plus an <see cref="HttpClient"/> used
/// to fetch each <c>files[].downloads[0]</c> URL. The importer never mutates a pre-existing
/// instance folder - it creates a fresh per-id subdirectory and writes everything in there.
/// </summary>
public sealed class ModrinthModpackImporter : IModpackImporter
{
    private const string ManifestEntry = ModpackFormatDetector.ModrinthManifestEntry;
    private const string OverridesPrefix = "overrides/";
    private const string ClientOverridesPrefix = "client-overrides/";

    /// <summary>Concurrent CDN downloads. Modrinth's CDN is fine with a handful; 6 matches browser per-host limits.</summary>
    private const int MaxParallelDownloads = 6;

    private readonly string _instancesRoot;
    private readonly HttpClient _http;

    /// <summary>
    /// Construct against a per-instance root directory + an HTTP client. The constructor
    /// only validates its arguments; the directory is created lazily on first import.
    /// </summary>
    public ModrinthModpackImporter(string instancesRoot, HttpClient http)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instancesRoot);
        _instancesRoot = instancesRoot;
        _http = http ?? throw new ArgumentNullException(nameof(http));
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
        await using (var fs = File.OpenRead(archivePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
        {
            manifest = ParseManifest(zip);
        }

        var instanceId = Guid.NewGuid().ToString("N");
        var instanceDir = Path.Combine(_instancesRoot, instanceId);
        Directory.CreateDirectory(instanceDir);

        var name = string.IsNullOrWhiteSpace(targetInstanceName) ? manifest.Name : targetInstanceName;

        try
        {
            // 下载客户端适用的整合包文件，包含可选文件。
            var required = manifest.Files;
            var total = Math.Max(1, manifest.Files.Count);
            var completed = 0;
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxParallelDownloads,
                CancellationToken = cancellationToken,
            };
            await Parallel.ForEachAsync(required, options, async (file, token) =>
            {
                var dest = ResolveInside(instanceDir, file.TargetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                await DownloadVerifiedAsync(file, dest, token).ConfigureAwait(false);
                progress?.Report((double)Interlocked.Increment(ref completed) / total);
            }).ConfigureAwait(false);

            // 2) Copy overrides/, then client-overrides/ on top of it (the spec's precedence).
            await using (var fs = File.OpenRead(archivePath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                CopyOverrides(zip, "overrides", instanceDir, cancellationToken);
                CopyOverrides(zip, "client-overrides", instanceDir, cancellationToken);
            }
        }
        catch
        {
            // Never leave a half-populated instance folder behind: it would sit on disk with
            // no instance record pointing at it.
            try { Directory.Delete(instanceDir, recursive: true); } catch { /* best-effort */ }
            throw;
        }

        // Final progress tick: importing is done.
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
    /// Parse <c>modrinth.index.json</c> out of an open zip archive. Public so tests can
    /// feed a synthetic zip without the importer touching the disk.
    /// </summary>
    public static ModpackManifest ParseManifest(ZipArchive zip)
    {
        ArgumentNullException.ThrowIfNull(zip);
        var entry = zip.GetEntry(ManifestEntry)
            ?? throw new InvalidDataException($"Missing '{ManifestEntry}' inside the .mrpack archive.");

        using var stream = entry.Open();
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        if (!root.TryGetProperty("formatVersion", out var format) || !format.TryGetInt32(out var formatVersion) || formatVersion != 1 || ReadString(root, "game") != "minecraft")
            throw new InvalidDataException("不支持的 mrpack 格式或游戏类型。");
        var name = ReadString(root, "name") ?? "Modpack";
        var version = ReadString(root, "versionId") ?? string.Empty;
        var (mcVersion, loader, loaderVersion) = ReadDependencies(root);
        var files = ReadFiles(root);
        if (string.IsNullOrWhiteSpace(mcVersion)) throw new InvalidDataException("mrpack 未指定 Minecraft 版本。");

        // Detect overrides folder by looking inside the zip - prefer overrides/, fall back to
        // client-overrides/. Server-overrides are ignored on the launcher side.
        string? overridesPath = null;
        foreach (var e in zip.Entries)
        {
            if (e.FullName.StartsWith(OverridesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                overridesPath = "overrides";
                break;
            }
        }
        if (overridesPath is null)
        {
            foreach (var e in zip.Entries)
            {
                if (e.FullName.StartsWith(ClientOverridesPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    overridesPath = "client-overrides";
                    break;
                }
            }
        }

        return new ModpackManifest
        {
            Name = name,
            Version = version,
            MinecraftVersion = mcVersion,
            Loader = loader,
            LoaderVersion = loaderVersion,
            Files = files,
            OverridesPath = overridesPath,
        };
    }

    private static (string MinecraftVersion, ModLoader Loader, string? LoaderVersion) ReadDependencies(JsonElement root)
    {
        if (!root.TryGetProperty("dependencies", out var deps) || deps.ValueKind != JsonValueKind.Object)
            return (string.Empty, ModLoader.None, null);

        var mcVersion = ReadString(deps, "minecraft") ?? string.Empty;

        // Modrinth's dependencies map encodes the loader as a kind-specific key. Only one
        // loader-key should be present per pack; we probe in priority order.
        if (deps.TryGetProperty("fabric-loader", out var f) && f.ValueKind == JsonValueKind.String)
            return (mcVersion, ModLoader.Fabric, f.GetString());
        if (deps.TryGetProperty("forge", out var forge) && forge.ValueKind == JsonValueKind.String)
            return (mcVersion, ModLoader.Forge, forge.GetString());
        if (deps.TryGetProperty("neoforge", out var neo) && neo.ValueKind == JsonValueKind.String)
            return (mcVersion, ModLoader.NeoForge, neo.GetString());
        if (deps.TryGetProperty("quilt-loader", out var quilt) && quilt.ValueKind == JsonValueKind.String)
            return (mcVersion, ModLoader.Quilt, quilt.GetString());

        return (mcVersion, ModLoader.None, null);
    }

    private static IReadOnlyList<ModpackFile> ReadFiles(JsonElement root)
    {
        if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("mrpack 缺少文件列表。");

        var list = new List<ModpackFile>(files.GetArrayLength());
        foreach (var file in files.EnumerateArray())
        {
            if (file.TryGetProperty("env", out var fileEnv) && fileEnv.ValueKind == JsonValueKind.Object &&
                string.Equals(ReadString(fileEnv, "client"), "unsupported", StringComparison.OrdinalIgnoreCase)) continue;
            var path = ReadString(file, "path");
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("mrpack 文件缺少路径。");
            // The spec requires launchers to refuse packs whose files point outside the instance.
            if (!IsSafeRelativePath(path))
                throw new InvalidDataException($"Unsafe file path '{path}' in {ManifestEntry}.");

            var urls = new List<string>();
            if (file.TryGetProperty("downloads", out var downloads) && downloads.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in downloads.EnumerateArray())
                {
                    if (d.ValueKind == JsonValueKind.String && d.GetString() is { Length: > 0 } u && !string.IsNullOrWhiteSpace(u))
                        urls.Add(u);
                }
            }
            if (urls.Count == 0) throw new InvalidDataException($"mrpack 文件缺少下载地址：{path}");

            string? sha1 = null;
            if (file.TryGetProperty("hashes", out var hashes) && hashes.ValueKind == JsonValueKind.Object)
                sha1 = ReadString(hashes, "sha1");

            if (string.IsNullOrWhiteSpace(sha1)) throw new InvalidDataException($"mrpack 文件缺少 SHA-1：{path}");

            long? size = null;
            if (file.TryGetProperty("fileSize", out var sizeEl) && sizeEl.ValueKind == JsonValueKind.Number)
                if (sizeEl.TryGetInt64(out var s)) size = s;

            // env.client = "required" | "optional" | "unsupported". Default to required when missing.
            var required = true;
            if (file.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object)
            {
                var client = ReadString(env, "client");
                if (string.Equals(client, "unsupported", StringComparison.OrdinalIgnoreCase))
                    continue; // skip server-only files entirely
                if (string.Equals(client, "optional", StringComparison.OrdinalIgnoreCase))
                    required = false;
            }

            list.Add(new ModpackFile
            {
                TargetPath = path!,
                DownloadUrl = urls[0],
                MirrorUrls = urls.Skip(1).ToList(),
                Sha1 = sha1,
                FileSizeBytes = size,
                Required = required,
            });
        }
        return list;
    }

    private static void CopyOverrides(ZipArchive zip, string overridesRoot, string instanceDir, CancellationToken cancellationToken)
    {
        var prefix = overridesRoot.EndsWith('/') ? overridesRoot : overridesRoot + "/";
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            // Directory entries (trailing slash) carry no bytes; just ensure the folder exists.
            var relative = entry.FullName.Substring(prefix.Length);
            if (string.IsNullOrEmpty(relative)) continue;
            if (!IsSafeRelativePath(relative.TrimEnd('/')))
                throw new InvalidDataException($"Unsafe override path '{entry.FullName}' in the .mrpack archive.");
            var dest = ResolveInside(instanceDir, relative.TrimEnd('/'));

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

    /// <summary>
    /// Fetch <paramref name="file"/> into <paramref name="destPath"/>, trying the primary URL and
    /// then each mirror. A download only counts when its sha1 matches the manifest (when given).
    /// </summary>
    private async Task DownloadVerifiedAsync(ModpackFile file, string destPath, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var url in file.MirrorUrls.Prepend(file.DownloadUrl))
        {
            try
            {
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                string actualSha1;
                await using (var src = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var fs = File.Create(destPath))
                {
                    using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await src.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        sha1.AppendData(buffer, 0, read);
                        await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    }
                    actualSha1 = Convert.ToHexString(sha1.GetHashAndReset());
                }

                if (string.Equals(actualSha1, file.Sha1, StringComparison.OrdinalIgnoreCase) &&
                    (file.FileSizeBytes is null || new FileInfo(destPath).Length == file.FileSizeBytes))
                    return;

                File.Delete(destPath);
                lastError = new InvalidDataException($"sha1 mismatch from {url} (expected {file.Sha1}, got {actualSha1.ToLowerInvariant()})");
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = ex; // HttpClient timeout, not a user cancel - try the next mirror
            }
        }
        throw new InvalidDataException($"Could not download '{file.TargetPath}': {lastError?.Message}", lastError);
    }

    /// <summary>
    /// True for a plain relative path that stays inside the instance: not rooted, no drive
    /// letter, no <c>..</c> segment. Checked on both manifest paths and override entries.
    /// </summary>
    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':'))
            return false;
        if (path.StartsWith('/') || path.StartsWith('\\'))
            return false;
        return path.Split('/', '\\').All(segment => segment != "..");
    }

    /// <summary>Combine and re-check that the result really lands under <paramref name="root"/>.</summary>
    private static string ResolveInside(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Path '{relativePath}' escapes the instance directory.");
        return full;
    }

    private static string? ReadString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
