using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Cache;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;

/// <summary>
/// Looks up a player's skin via Mojang's session server:
/// <c>https://sessionserver.mojang.com/session/minecraft/profile/{uuid}?unsigned=false</c>
/// then base64-decodes the <c>textures</c> property to find the skin / cape URLs and
/// downloads the PNGs. Network or schema failures degrade silently to <c>null</c> so
/// the UI keeps the default Steve.
/// </summary>
public sealed class MojangPlayerSkinFetcher : IPlayerSkinFetcher
{
    private const string ProfileUrlTemplate =
        "https://sessionserver.mojang.com/session/minecraft/profile/{0}?unsigned=false";

    /// <summary>Cache TTL: 6 hours. Skins change rarely; on a stale hit we still re-fetch
    /// in the background by deleting older entries.</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    private readonly HttpClient _http;
    private readonly FileCache? _cache;

    public MojangPlayerSkinFetcher() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }) { }

    public MojangPlayerSkinFetcher(HttpClient http, FileCache? cache = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<PlayerSkinInfo?> FetchAsync(string uuid, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(uuid)) return null;

        var trimmed = uuid.Replace("-", string.Empty);
        if (trimmed.Length != 32) return null;

        // Cache hit: load skin PNG + sidecar metadata (slim flag + cape ref) from disk.
        if (_cache is not null)
        {
            var skinKey = $"skins/{trimmed}.png";
            var metaKey = $"skins/{trimmed}.json";
            var skin = await _cache.TryReadAsync(skinKey, CacheTtl, cancellationToken).ConfigureAwait(false);
            var meta = skin is null ? null : await _cache.TryReadAsync(metaKey, CacheTtl, cancellationToken).ConfigureAwait(false);
            if (skin is { Length: > 0 } && meta is { Length: > 0 })
            {
                try
                {
                    using var doc = JsonDocument.Parse(meta);
                    bool slim = doc.RootElement.TryGetProperty("slim", out var s) && s.GetBoolean();
                    string? cape = doc.RootElement.TryGetProperty("cape", out var c) && c.ValueKind == JsonValueKind.String
                        ? c.GetString() : null;
                    byte[]? capeBytes = null;
                    if (!string.IsNullOrEmpty(cape))
                        capeBytes = await _cache.TryReadAsync($"skins/{trimmed}.cape.png", CacheTtl, cancellationToken).ConfigureAwait(false);
                    return new PlayerSkinInfo { SkinPng = skin, IsSlim = slim, CapePng = capeBytes };
                }
                catch
                {
                    // bad cache - fall through to network
                }
            }
        }

        try
        {
            var url = string.Format(ProfileUrlTemplate, trimmed.ToLowerInvariant());
            using var resp = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var profile = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            // properties: [{ name: "textures", value: <base64> }]
            if (!profile.RootElement.TryGetProperty("properties", out var props)
                || props.ValueKind != JsonValueKind.Array)
                return null;

            string? texturesB64 = null;
            foreach (var p in props.EnumerateArray())
            {
                if (p.TryGetProperty("name", out var n) && n.GetString() == "textures"
                    && p.TryGetProperty("value", out var v) && v.GetString() is { } s)
                {
                    texturesB64 = s;
                    break;
                }
            }
            if (texturesB64 is null) return null;

            var inner = Encoding.UTF8.GetString(Convert.FromBase64String(texturesB64));
            using var texturesDoc = JsonDocument.Parse(inner);
            if (!texturesDoc.RootElement.TryGetProperty("textures", out var textures)
                || textures.ValueKind != JsonValueKind.Object)
                return null;

            string? skinUrl = null;
            bool slim = false;
            if (textures.TryGetProperty("SKIN", out var skinNode) && skinNode.ValueKind == JsonValueKind.Object)
            {
                if (skinNode.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
                    skinUrl = u.GetString();
                if (skinNode.TryGetProperty("metadata", out var meta)
                    && meta.ValueKind == JsonValueKind.Object
                    && meta.TryGetProperty("model", out var model)
                    && model.ValueKind == JsonValueKind.String
                    && string.Equals(model.GetString(), "slim", StringComparison.OrdinalIgnoreCase))
                {
                    slim = true;
                }
            }
            if (string.IsNullOrEmpty(skinUrl)) return null;

            var skinPng = await _http.GetByteArrayAsync(skinUrl, cancellationToken).ConfigureAwait(false);

            byte[]? capePng = null;
            if (textures.TryGetProperty("CAPE", out var capeNode)
                && capeNode.ValueKind == JsonValueKind.Object
                && capeNode.TryGetProperty("url", out var cu)
                && cu.ValueKind == JsonValueKind.String
                && cu.GetString() is { Length: > 0 } capeUrl)
            {
                try
                {
                    capePng = await _http.GetByteArrayAsync(capeUrl, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    capePng = null;
                }
            }

            var result = new PlayerSkinInfo { SkinPng = skinPng, IsSlim = slim, CapePng = capePng };

            // Best-effort cache write so the next launch is instant.
            if (_cache is not null)
            {
                try
                {
                    await _cache.WriteAsync($"skins/{trimmed}.png", skinPng, cancellationToken).ConfigureAwait(false);
                    var meta = JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        slim,
                        cape = capePng is null ? null : "yes",
                        fetched = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    });
                    await _cache.WriteAsync($"skins/{trimmed}.json", meta, cancellationToken).ConfigureAwait(false);
                    if (capePng is not null)
                        await _cache.WriteAsync($"skins/{trimmed}.cape.png", capePng, cancellationToken).ConfigureAwait(false);
                }
                catch { /* cache write is best-effort */ }
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Network failure - try a stale cache entry as a last resort so the user sees
            // their own skin offline (we already validated trimmed is 32 chars).
            if (_cache is not null)
            {
                var skin = await _cache.TryReadAsync($"skins/{trimmed}.png", maxAge: null, cancellationToken).ConfigureAwait(false);
                var meta = skin is null ? null : await _cache.TryReadAsync($"skins/{trimmed}.json", maxAge: null, cancellationToken).ConfigureAwait(false);
                if (skin is { Length: > 0 })
                {
                    bool slim = false;
                    if (meta is { Length: > 0 })
                    {
                        try { using var doc = JsonDocument.Parse(meta); slim = doc.RootElement.TryGetProperty("slim", out var s) && s.GetBoolean(); }
                        catch { }
                    }
                    return new PlayerSkinInfo { SkinPng = skin, IsSlim = slim, CapePng = null };
                }
            }
            return null;
        }
    }
}
