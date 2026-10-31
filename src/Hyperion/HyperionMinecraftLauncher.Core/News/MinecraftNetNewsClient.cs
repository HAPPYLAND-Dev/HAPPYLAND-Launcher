using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Cache;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.News;

/// <summary>
/// Reads the article list behind <c>https://www.minecraft.net/en-us/article</c>: the site's
/// own grid is filled from this search endpoint, so the launcher shows exactly what the site
/// shows, newest first. Same caching contract as <see cref="MojangNewsClient"/>: one hour
/// fresh, any stale copy served when the network is down, empty list otherwise.
/// </summary>
public sealed class MinecraftNetNewsClient : INewsClient
{
    private const int PageSize = 24; // the API rejects anything above 24 with 400
    private const string SearchUrl =
        "https://net-secondary.web.minecraft-services.net/api/v1.0/en-us/search?pageSize=24&sortType=Recent&category=News";
    private const string SiteBaseUrl = "https://www.minecraft.net";
    private const string CacheKey = "minecraftnet-news.json";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly HttpClient _http;
    private readonly FileCache? _cache;

    /// <summary>Use a caller-configured client and an optional disk cache.</summary>
    public MinecraftNetNewsClient(HttpClient http, FileCache? cache = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NewsEntry>> FetchAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null
            && await _cache.TryReadAsync(CacheKey, CacheTtl, cancellationToken).ConfigureAwait(false) is { Length: > 0 } fresh
            && TryParse(fresh) is { Count: > 0 } cached)
            return cached;

        try
        {
            var bytes = await _http.GetByteArrayAsync(SearchUrl, cancellationToken).ConfigureAwait(false);
            var entries = Parse(new MemoryStream(bytes));
            if (_cache is not null && entries.Count > 0)
            {
                try { await _cache.WriteAsync(CacheKey, bytes, cancellationToken).ConfigureAwait(false); } catch { /* best-effort */ }
            }
            return entries;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            if (_cache is not null
                && await _cache.TryReadAsync(CacheKey, maxAge: null, cancellationToken).ConfigureAwait(false) is { Length: > 0 } stale)
                return TryParse(stale) ?? Array.Empty<NewsEntry>();
            return Array.Empty<NewsEntry>();
        }
    }

    /// <summary>Parse one search-API page. Public for direct unit tests.</summary>
    public static IReadOnlyList<NewsEntry> Parse(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array)
            return Array.Empty<NewsEntry>();

        var list = new List<NewsEntry>(Math.Min(results.GetArrayLength(), PageSize));
        foreach (var el in results.EnumerateArray())
        {
            var title = ReadString(el, "title");
            var url = ReadString(el, "url");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url)) continue;

            var date = el.TryGetProperty("time", out var t) && t.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("yyyy-MM-dd")
                : string.Empty;

            list.Add(new NewsEntry
            {
                Title = title.Trim(),
                Category = CategoryFromTags(el),
                Date = date,
                Text = (ReadString(el, "description") ?? string.Empty).Trim(),
                ImageUrl = Absolute(ReadString(el, "image")),
                ReadMoreLink = url,
                Id = url,
            });
        }
        return list;
    }

    /// <summary>
    /// The site tags every article with <c>minecraft:games/&lt;game&gt;</c> or a marketplace area;
    /// turn the first one into the same kind of label the launcher feed used.
    /// </summary>
    private static string CategoryFromTags(JsonElement el)
    {
        if (!el.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
            return "Minecraft";
        foreach (var tag in tags.EnumerateArray())
        {
            var value = tag.ValueKind == JsonValueKind.String ? tag.GetString() : null;
            if (value is null) continue;
            if (value.StartsWith("minecraft:marketplace/realms", StringComparison.Ordinal)) return "Realms";
            if (value.StartsWith("minecraft:marketplace", StringComparison.Ordinal)) return "Marketplace";
            if (!value.StartsWith("minecraft:games/", StringComparison.Ordinal)) continue;

            var game = value["minecraft:games/".Length..].Split('/')[0];
            return game switch
            {
                "minecraft-java" => "Minecraft: Java Edition",
                "minecraft-bedrock" => "Minecraft: Bedrock Edition",
                "minecraft-dungeons-ii" => "Minecraft Dungeons II",
                "minecraft-dungeons" => "Minecraft Dungeons",
                "minecraft-legends" => "Minecraft Legends",
                "minecraft-education" => "Minecraft Education",
                _ => "Minecraft",
            };
        }
        return "Minecraft";
    }

    private static string? Absolute(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : SiteBaseUrl + (url.StartsWith('/') ? url : "/" + url);
    }

    private static IReadOnlyList<NewsEntry>? TryParse(byte[] bytes)
    {
        try { return Parse(new MemoryStream(bytes)); }
        catch (JsonException) { return null; }
    }

    private static string? ReadString(JsonElement el, string property)
        => el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
