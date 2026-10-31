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
/// Fetches the Mojang launcher's own news feed (<c>https://launchercontent.mojang.com/v2/news.json</c>).
/// This is the same endpoint the official Minecraft launcher polls to populate its Play / News pages.
/// The v1 <c>news.json</c> still answers but stopped updating in January 2024; v2 is the live one
/// (images live under <c>/v2/images/</c>, resolved against the same host).
/// </summary>
public sealed class MojangNewsClient : INewsClient
{
    private const string FeedUrl = "https://launchercontent.mojang.com/v2/news.json";
    private const string ImageBaseUrl = "https://launchercontent.mojang.com";
    private const string CacheKey = "news.v2.json"; // new key: never serve the frozen v1 copy
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly HttpClient _http;
    private readonly FileCache? _cache;

    /// <summary>Default constructor: builds its own <see cref="HttpClient"/>; no disk cache.</summary>
    public MojangNewsClient() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }) { }

    /// <summary>Test / advanced constructor accepting a pre-configured client (or one with a fake handler) and an optional disk cache.</summary>
    public MojangNewsClient(HttpClient http, FileCache? cache = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NewsEntry>> FetchAsync(CancellationToken cancellationToken)
    {
        // Cache hit (fresh): return parsed without hitting the network.
        if (_cache is not null)
        {
            var cached = await _cache.TryReadAsync(CacheKey, CacheTtl, cancellationToken).ConfigureAwait(false);
            if (cached is { Length: > 0 })
            {
                try
                {
                    using var ms = new MemoryStream(cached);
                    return Parse(ms);
                }
                catch
                {
                    // Corrupt cache entry - fall through and re-fetch.
                }
            }
        }

        try
        {
            var bytes = await _http.GetByteArrayAsync(FeedUrl, cancellationToken).ConfigureAwait(false);
            if (_cache is not null && bytes.Length > 0)
            {
                try { await _cache.WriteAsync(CacheKey, bytes, cancellationToken).ConfigureAwait(false); } catch { /* best-effort */ }
            }
            using var ms = new MemoryStream(bytes);
            return Parse(ms);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Network outage / schema drift: if we have ANY cached entry (even stale), serve it
            // so the user sees yesterday's news offline rather than a blank page.
            if (_cache is not null)
            {
                var stale = await _cache.TryReadAsync(CacheKey, maxAge: null, cancellationToken).ConfigureAwait(false);
                if (stale is { Length: > 0 })
                {
                    try { using var ms = new MemoryStream(stale); return Parse(ms); } catch { }
                }
            }
            return Array.Empty<NewsEntry>();
        }
    }

    /// <summary>Parse a JSON stream in the launcher news schema. Public for direct unit tests.</summary>
    public static IReadOnlyList<NewsEntry> Parse(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return Array.Empty<NewsEntry>();

        var list = new List<NewsEntry>(entries.GetArrayLength());
        foreach (var el in entries.EnumerateArray())
        {
            var title = ReadString(el, "title") ?? string.Empty;
            var category = ReadString(el, "category") ?? string.Empty;
            var date = ReadString(el, "date") ?? string.Empty;
            var text = ReadString(el, "text") ?? string.Empty;
            var image = ReadImageUrl(el, "newsPageImage") ?? ReadImageUrl(el, "playPageImage");
            list.Add(new NewsEntry
            {
                Title = title,
                Category = category,
                Date = date,
                Text = text,
                ImageUrl = image,
                ReadMoreLink = ReadString(el, "readMoreLink"),
                Id = ReadString(el, "id"),
            });
        }
        return list;
    }

    private static string? ReadString(JsonElement el, string property)
        => el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string? ReadImageUrl(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var img) || img.ValueKind != JsonValueKind.Object)
            return null;
        var url = ReadString(img, "url");
        if (string.IsNullOrEmpty(url))
            return null;
        // The feed gives relative paths like "/images/...". Resolve against the CDN.
        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? url
            : ImageBaseUrl + url;
    }
}
