using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.Modrinth;

/// <summary>
/// <see cref="IModRepository"/> backed by the public Modrinth v2 API
/// (<c>https://api.modrinth.com/v2</c>). No API key required.
/// </summary>
/// <remarks>
/// Modrinth's facets are JSON-array-of-JSON-array syntax encoded as a query-string parameter
/// (e.g. <c>facets=[["versions:1.20.1"],["categories:fabric"]]</c>). The outer pairs are
/// AND-joined; the inner pairs are OR-joined - which is exactly what we want for the
/// "must run on 1.20.1 AND must be a Fabric mod" gameplay filter.
/// </remarks>
public sealed class ModrinthRepository : IModRepository
{
    /// <summary>Base URL for the Modrinth v2 REST API.</summary>
    public const string DefaultBaseAddress = "https://api.modrinth.com/v2/";

    /// <summary>Identifies us to Modrinth so requests can be throttled / contacted on misuse.</summary>
    public const string UserAgent = "HyperionMinecraftLauncher/0.27.0 (contact@techteastudio.cc)";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    /// <summary>
    /// Construct against any pre-configured <see cref="HttpClient"/>. The base address
    /// is set to <see cref="DefaultBaseAddress"/> when missing - tests typically pass in
    /// a client backed by a mock <c>HttpMessageHandler</c> with the same base address.
    /// </summary>
    public ModrinthRepository(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(DefaultBaseAddress);
        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd(UserAgent))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
    }

    /// <inheritdoc />
    public ModSource Source => ModSource.Modrinth;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Mod>> SearchAsync(ModSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sb = new StringBuilder("search?");
        sb.Append("query=").Append(Uri.EscapeDataString(query.Query ?? string.Empty));
        sb.Append("&limit=").Append(query.Limit);
        sb.Append("&offset=").Append(query.Offset);
        // No text to rank by: list the most downloaded projects, like modrinth.com/mods does.
        if (string.IsNullOrWhiteSpace(query.Query))
            sb.Append("&index=downloads");

        var facets = BuildFacets(query);
        if (facets is not null)
        {
            sb.Append("&facets=").Append(Uri.EscapeDataString(facets));
        }

        using var response = await _http.GetAsync(sb.ToString(), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return ParseSearchHits(stream);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModFile>> ListFilesAsync(
        string modId,
        string? gameVersion,
        ModLoader? loader,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);

        var sb = new StringBuilder("project/").Append(Uri.EscapeDataString(modId)).Append("/version");
        var first = true;
        if (!string.IsNullOrEmpty(gameVersion))
        {
            sb.Append(first ? '?' : '&').Append("game_versions=").Append(Uri.EscapeDataString("[\"" + gameVersion + "\"]"));
            first = false;
        }
        if (loader is { } l && l != ModLoader.None)
        {
            var name = LoaderToFacet(l);
            if (name is not null)
            {
                sb.Append(first ? '?' : '&').Append("loaders=").Append(Uri.EscapeDataString("[\"" + name + "\"]"));
            }
        }

        using var response = await _http.GetAsync(sb.ToString(), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return ParseFiles(stream, modId);
    }

    /// <inheritdoc />
    public async Task DownloadAsync(ModFile file, Stream destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(destination);

        using var request = new HttpRequestMessage(HttpMethod.Get, file.DownloadUrl);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? file.FileSize;
        await using var src = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new byte[81920];
        long readSoFar = 0;
        int read;
        while ((read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            readSoFar += read;
            if (progress is not null && total > 0)
                progress.Report((double)readSoFar / total);
        }
    }

    private static string? BuildFacets(ModSearchQuery query)
    {
        var groups = new List<string>();

        if (!string.IsNullOrEmpty(query.GameVersion))
            groups.Add($"[\"versions:{query.GameVersion}\"]");

        if (query.Loader is { } loader && loader != ModLoader.None)
        {
            var f = LoaderToFacet(loader);
            if (f is not null)
                groups.Add($"[\"categories:{f}\"]");
        }

        if (!string.IsNullOrEmpty(query.Category))
            groups.Add($"[\"categories:{query.Category}\"]");

        // Always pin project_type to "mod" so the launcher doesn't return resource packs / plugins.
        groups.Add("[\"project_type:mod\"]");

        if (groups.Count == 0) return null;
        return "[" + string.Join(",", groups) + "]";
    }

    /// <summary>Modrinth's "categories" facet uses the lowercase loader name.</summary>
    private static string? LoaderToFacet(ModLoader loader) => loader switch
    {
        ModLoader.Fabric => "fabric",
        ModLoader.Forge => "forge",
        ModLoader.NeoForge => "neoforge",
        ModLoader.Quilt => "quilt",
        ModLoader.LegacyForge => "forge",
        _ => null,
    };

    /// <summary>Parse a Modrinth <c>/search</c> JSON payload. Public for direct unit tests.</summary>
    public static IReadOnlyList<Mod> ParseSearchHits(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
            return Array.Empty<Mod>();

        var list = new List<Mod>(hits.GetArrayLength());
        foreach (var el in hits.EnumerateArray())
        {
            var id = ReadString(el, "project_id") ?? ReadString(el, "id") ?? string.Empty;
            if (string.IsNullOrEmpty(id)) continue;

            list.Add(new Mod
            {
                Id = id,
                Slug = ReadString(el, "slug") ?? id,
                Name = ReadString(el, "title") ?? id,
                Description = ReadString(el, "description") ?? string.Empty,
                AuthorDisplay = ReadString(el, "author") ?? string.Empty,
                IconUri = ReadString(el, "icon_url"),
                PageUri = $"https://modrinth.com/mod/{ReadString(el, "slug") ?? id}",
                Categories = ReadStringArray(el, "categories"),
                Downloads = ReadInt64(el, "downloads"),
                Source = ModSource.Modrinth,
            });
        }
        return list;
    }

    /// <summary>Parse a Modrinth <c>/version</c> JSON payload. Public for direct unit tests.</summary>
    public static IReadOnlyList<ModFile> ParseFiles(Stream stream, string modId)
    {
        using var doc = JsonDocument.Parse(stream);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return Array.Empty<ModFile>();

        var list = new List<ModFile>(doc.RootElement.GetArrayLength());
        foreach (var v in doc.RootElement.EnumerateArray())
        {
            var fileId = ReadString(v, "id") ?? string.Empty;
            if (string.IsNullOrEmpty(fileId)) continue;

            var displayName = ReadString(v, "version_number") ?? ReadString(v, "name") ?? fileId;
            var gameVersions = ReadStringArray(v, "game_versions");
            var loaderNames = ReadStringArray(v, "loaders");
            var loaders = new List<ModLoader>(loaderNames.Count);
            foreach (var n in loaderNames)
                if (TryParseLoader(n, out var l))
                    loaders.Add(l);

            if (!v.TryGetProperty("files", out var filesEl) || filesEl.ValueKind != JsonValueKind.Array)
                continue;

            // Pick the "primary" file, falling back to the first.
            JsonElement chosen = default;
            var found = false;
            foreach (var f in filesEl.EnumerateArray())
            {
                if (!found) { chosen = f; found = true; }
                if (f.TryGetProperty("primary", out var prim) && prim.ValueKind == JsonValueKind.True)
                {
                    chosen = f;
                    break;
                }
            }
            if (!found) continue;

            var url = ReadString(chosen, "url") ?? string.Empty;
            if (string.IsNullOrEmpty(url)) continue;
            var filename = ReadString(chosen, "filename");
            long size = ReadInt64(chosen, "size");
            string? sha1 = null;
            if (chosen.TryGetProperty("hashes", out var hashes) && hashes.ValueKind == JsonValueKind.Object)
                sha1 = ReadString(hashes, "sha1");

            list.Add(new ModFile
            {
                ModId = modId,
                FileId = fileId,
                DisplayName = displayName,
                Filename = filename,
                DownloadUrl = url,
                GameVersions = gameVersions,
                Loaders = loaders,
                FileSize = size,
                Sha1 = sha1,
            });
        }
        return list;
    }

    private static bool TryParseLoader(string name, out ModLoader loader)
    {
        switch (name?.ToLowerInvariant())
        {
            case "fabric": loader = ModLoader.Fabric; return true;
            case "forge": loader = ModLoader.Forge; return true;
            case "neoforge": loader = ModLoader.NeoForge; return true;
            case "quilt": loader = ModLoader.Quilt; return true;
            default: loader = ModLoader.None; return false;
        }
    }

    private static string? ReadString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Null => null,
            _ => null,
        };
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

    private static IReadOnlyList<string> ReadStringArray(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>(v.GetArrayLength());
        foreach (var s in v.EnumerateArray())
            if (s.ValueKind == JsonValueKind.String)
                list.Add(s.GetString() ?? string.Empty);
        return list;
    }
}
