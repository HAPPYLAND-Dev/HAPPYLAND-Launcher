using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Browser;

/// <summary>
/// <see cref="ISkinBrowser"/> backed by the public <c>https://api.mineskin.org/v2/skins</c>
/// REST endpoint. Replaces <see cref="NameMcSkinBrowser"/> in v0.32.3 because NameMC's
/// Cloudflare layer now serves a JavaScript challenge (<c>cf-mitigated=challenge</c>) to
/// every scripted request, so pure HTTP cannot reach the gallery without a real browser.
/// </summary>
/// <remarks>
/// <para>
/// MineSkin's v2 list endpoint returns clean JSON with no anti-bot wall: each entry has
/// <c>uuid</c>, <c>shortId</c> (8-char ID for the <c>https://minesk.in/{shortId}</c> page),
/// <c>name</c> (often <c>null</c>), <c>texture</c> (the 64-char hash on
/// <c>textures.minecraft.net/texture/{hash}</c>) and a <c>timestamp</c>. The PNG download
/// URL is the same CDN every Mojang account ships its active skin from.
/// </para>
/// <para>
/// MineSkin does <strong>not</strong> publish per-skin model (Classic vs Slim) on the
/// anonymous feed, so every browsed skin is surfaced as <see cref="SkinVariant.Classic"/>.
/// The user can still pick a slim variant from the dedicated Upload control if they need to.
/// </para>
/// <para>
/// MineSkin's anonymous tier ignores the <c>?name=</c> filter (it issues a warning and
/// returns the unfiltered feed). To make search useful without auth we fall back to a
/// client-side filter on the entry's <see cref="MineSkinEntry.Name"/> and
/// <see cref="MineSkinEntry.ShortId"/> substring after pulling a larger page from the API.
/// </para>
/// </remarks>
public sealed class MineSkinBrowser : ISkinBrowser
{
    /// <summary>v2 listing endpoint stem (paginated, size-capped at 1000 by the server).</summary>
    public const string ListUrlStem = "https://api.mineskin.org/v2/skins";

    /// <summary>Direct CDN for raw skin PNGs.</summary>
    public const string TextureCdn = "https://textures.minecraft.net/texture/";

    /// <summary>Public web page for a skin, useful for "Open in browser" tooltips.</summary>
    public const string WebPageStem = "https://minesk.in/";

    /// <summary>How many extra entries to pull when the caller asks for a client-side search.</summary>
    /// <remarks>
    /// MineSkin v2 ignores the <c>?name=</c> filter without an API key, so we pull a larger
    /// page (up to <c>limit * 8</c>, capped at <see cref="MaxApiPageSize"/>) and filter
    /// locally. The cap is the smallest page that gives a search a fighting chance without
    /// slowing the UI.
    /// </remarks>
    public const int SearchPageMultiplier = 8;

    /// <summary>
    /// Hard upper bound on a single API page we request. The MineSkin v2 documentation lists
    /// the server-side cap as 1000 but the anonymous tier (no API key) actually rejects any
    /// <c>?size=</c> over 128 with <c>400 validation_error / too_big "Number must be less
    /// than or equal to 128 (size)"</c> - observed 2026-05-19, see the log "01:02:25 Не
    /// удалось загрузить галерею скинов: 400 (Bad Request)". The previous value (200) made
    /// every search call 400, since <c>SearchAsync</c> oversamples to <c>limit * 8</c> and
    /// clamps to this cap. Keep at 128 until the launcher carries an API key.
    /// </summary>
    public const int MaxApiPageSize = 128;

    /// <summary>Mojang's username -> UUID resolver. Anonymous and unmetered up to ~600 req/10 min.</summary>
    /// <remarks>
    /// I1 (v0.32.5): nickname search hybrid. MineSkin's gallery is an upload feed, not a
    /// directory of every player's current skin, so a username query like "Notch" rarely
    /// matches an entry. To make search useful we resolve the username through Mojang's
    /// public profile API and prepend the live skin to whatever MineSkin's filter returned.
    /// </remarks>
    public const string MojangUsernameLookupStem =
        "https://api.mojang.com/users/profiles/minecraft/";

    /// <summary>Mojang's session server: resolves a UUID to the base64-encoded textures blob.</summary>
    public const string MojangSessionProfileStem =
        "https://sessionserver.mojang.com/session/minecraft/profile/";

    /// <summary>NameMC vanity profile page for a UUID. Surfaced as the <c>SourceUrl</c> on Mojang results.</summary>
    public const string NameMcProfileStem = "https://namemc.com/profile/";

    /// <summary>
    /// Minecraft username regex: 3-16 alphanumeric + underscore. Matches Mojang's
    /// own validation since 2010 and rejects anything that obviously isn't a username
    /// (spaces, dashes, dots) so we don't burn a Mojang request on garbage input.
    /// </summary>
    private static readonly Regex UsernameShape =
        new(@"^[A-Za-z0-9_]{3,16}$", RegexOptions.Compiled);

    /// <summary>
    /// 32-char hex UUID with or without dashes. Matches the canonical
    /// <c>8-4-4-4-12</c> grouping and the bare 32-char form Mojang's APIs accept.
    /// </summary>
    private static readonly Regex UuidShape =
        new(@"^[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}$",
            RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly IPlayerSkinFetcher? _mojangFallbackCapability;

    /// <summary>Construct against an arbitrary <see cref="HttpClient"/>.</summary>
    /// <remarks>
    /// MineSkin's API has no anti-bot wall, so the launcher's default headers from the
    /// F2 work (Chrome-shaped UA + Accept-Language + Sec-Fetch-*) are accepted unchanged.
    /// The launcher reuses the same <c>HttpClient</c> instance that previously fed the
    /// NameMC browser. We don't reconfigure anything here.
    /// </remarks>
    public MineSkinBrowser(HttpClient http) : this(http, mojangFallback: null) { }

    /// <summary>
    /// Construct with the hybrid Mojang fallback enabled. When <paramref name="mojangFallback"/>
    /// is non-null, <see cref="SearchAsync(string, int, int, CancellationToken)"/> first tries
    /// Mojang's public username / UUID profile endpoints; on a hit, the result is prepended
    /// to MineSkin's filtered page so "search by nickname" actually returns the player's
    /// current skin instead of an empty gallery.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="IPlayerSkinFetcher"/> parameter is used as a *capability flag* only:
    /// its contract (UUID -> raw PNG bytes) is not the shape we need for a gallery card
    /// (we need username -> texture URL without downloading the PNG twice). So the actual
    /// HTTP work happens inline below, using <paramref name="http"/>. We accept the
    /// fetcher rather than a plain bool so callers can't accidentally enable the fallback
    /// without having wired the rest of the Mojang stack in DI.
    /// </para>
    /// <para>
    /// Mojang's username lookup is rate-limited to roughly 600 req/10 min for anonymous
    /// callers. Aggressive search-as-you-type would burn through that quota; the launcher
    /// only hits Mojang on explicit search (Enter / search button), and a 429 / 5xx response
    /// falls back to MineSkin's client-side filter without surfacing the failure to the user.
    /// </para>
    /// </remarks>
    public MineSkinBrowser(HttpClient http, IPlayerSkinFetcher? mojangFallback)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _mojangFallbackCapability = mojangFallback;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BrowsedSkin>> ListTrendingAsync(int limit, CancellationToken cancellationToken)
        => ListTrendingAsync(page: 0, limit, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// MineSkin v2 exposes <c>?page=N&amp;size=M</c> on <c>/v2/skins</c>; page indices are zero-based.
    /// The server caps <c>size</c> at 1000 but we clamp client-side at <see cref="MaxApiPageSize"/>
    /// (200) so a single page render stays responsive. Negative <paramref name="page"/> is treated
    /// as 0; non-positive <paramref name="limit"/> falls back to the default 60-card page.
    /// </remarks>
    public async Task<IReadOnlyList<BrowsedSkin>> ListTrendingAsync(int page, int limit, CancellationToken cancellationToken)
    {
        var safePage = Math.Max(0, page);
        var pageSize = Math.Clamp(limit > 0 ? limit : 60, 1, MaxApiPageSize);
        var url = $"{ListUrlStem}?page={safePage}&size={pageSize}";
        var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        return ParseList(json, limit, query: null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BrowsedSkin>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
        => SearchAsync(query, page: 0, limit, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// MineSkin's anonymous tier ignores the <c>?name=</c> filter, so we still oversample and
    /// filter client-side as in the unpaged path. The <c>?page=</c> parameter is passed through
    /// to the server unchanged: when the user pastes an API key in the future, server-side
    /// paging just works.
    /// </para>
    /// <para>
    /// I1 (v0.32.5): when the ctor was wired with a Mojang fallback, the search first tries
    /// Mojang's public profile API. The launcher Skins page surfaces a true username search
    /// rather than a gallery substring match - matching the user's mental model of "find my
    /// friend's skin". Mojang hits are prepended to whatever MineSkin's client-side filter
    /// produced. Mojang failures (404 / 429 / 5xx / parse error) degrade silently to the
    /// MineSkin-only path so search keeps working even when Mojang rate-limits us.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<BrowsedSkin>> SearchAsync(string query, int page, int limit, CancellationToken cancellationToken)
    {
        var q = query?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(q))
            return await ListTrendingAsync(page, limit, cancellationToken).ConfigureAwait(false);

        var safePage = Math.Max(0, page);

        // Pull a larger page so the client-side filter has more to chew on. We still hand
        // MineSkin the name filter even though the anonymous tier ignores it - the moment
        // the user pastes an API key (a future onboarding affordance) the server-side
        // filter takes effect.
        var pageSize = Math.Clamp(limit > 0 ? limit * SearchPageMultiplier : MaxApiPageSize, 1, MaxApiPageSize);
        var url = $"{ListUrlStem}?page={safePage}&size={pageSize}&name={Uri.EscapeDataString(q)}";
        var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        var fromMineSkin = ParseList(json, limit, query: q);

        // I1 (v0.32.5): hybrid Mojang fallback. Only kicks in when the ctor was given a
        // capability marker, only on page 0 (live player skin is a "top of search" affordance,
        // not paginated content), and only when the query looks like a username or UUID.
        if (_mojangFallbackCapability is null || safePage != 0)
            return fromMineSkin;

        var live = await TryResolveLivePlayerSkinAsync(q, cancellationToken).ConfigureAwait(false);
        if (live is null) return fromMineSkin;

        // Prepend, de-duplicating against any MineSkin entry that happens to share the
        // UUID (rare: MineSkin sometimes archives a skin under the player's UUID).
        var merged = new List<BrowsedSkin>(fromMineSkin.Count + 1) { live };
        foreach (var s in fromMineSkin)
        {
            if (!string.Equals(s.Id, live.Id, StringComparison.OrdinalIgnoreCase))
                merged.Add(s);
        }
        return merged;
    }

    /// <summary>
    /// Try to resolve <paramref name="query"/> to a live player skin through Mojang's APIs.
    /// Returns a single <see cref="BrowsedSkin"/> with <see cref="BrowsedSkin.IsLivePlayerSkin"/>
    /// set to true, or null if the query doesn't look like a username / UUID, the lookup
    /// returned 404 / 429 / 5xx, or the response shape was unexpected.
    /// </summary>
    /// <remarks>
    /// Two-step lookup for a username:
    /// <list type="number">
    /// <item><description><c>GET {MojangUsernameLookupStem}/{username}</c> -> <c>{ id, name }</c></description></item>
    /// <item><description><c>GET {MojangSessionProfileStem}/{uuid}</c> -> profile with base64 textures property</description></item>
    /// </list>
    /// A 32-char hex UUID skips step 1. Network failures bubble back as null; the caller
    /// is responsible for keeping the MineSkin path alive.
    /// </remarks>
    private async Task<BrowsedSkin?> TryResolveLivePlayerSkinAsync(string query, CancellationToken cancellationToken)
    {
        string? uuid = null;
        string? canonicalName = null;

        if (UuidShape.IsMatch(query))
        {
            uuid = query.Replace("-", string.Empty).ToLowerInvariant();
        }
        else if (UsernameShape.IsMatch(query))
        {
            try
            {
                var lookupUrl = MojangUsernameLookupStem + Uri.EscapeDataString(query);
                using var lookupResp = await _http.GetAsync(lookupUrl, cancellationToken).ConfigureAwait(false);
                // 404 = no such player, 204 = legacy "no profile", 429 = rate-limited.
                // All of these fall back to MineSkin without surfacing an error.
                if (!lookupResp.IsSuccessStatusCode) return null;
                await using var lookupStream = await lookupResp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var lookupDoc = await JsonDocument.ParseAsync(lookupStream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (lookupDoc.RootElement.ValueKind != JsonValueKind.Object) return null;
                if (lookupDoc.RootElement.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    uuid = idEl.GetString();
                if (lookupDoc.RootElement.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                    canonicalName = nameEl.GetString();
            }
            catch (OperationCanceledException) { throw; }
            catch (HttpRequestException) { return null; }
            catch (JsonException) { return null; }
        }
        else
        {
            // Neither username shape nor UUID shape - nothing to look up.
            return null;
        }

        if (string.IsNullOrEmpty(uuid) || uuid.Length != 32) return null;
        canonicalName ??= query;

        try
        {
            var profileUrl = MojangSessionProfileStem + uuid;
            using var profileResp = await _http.GetAsync(profileUrl, cancellationToken).ConfigureAwait(false);
            if (!profileResp.IsSuccessStatusCode) return null;
            await using var profileStream = await profileResp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var profileDoc = await JsonDocument.ParseAsync(profileStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!profileDoc.RootElement.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Array)
                return null;
            string? texturesB64 = null;
            foreach (var p in props.EnumerateArray())
            {
                if (p.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() == "textures"
                    && p.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
                {
                    texturesB64 = v.GetString();
                    break;
                }
            }
            if (string.IsNullOrEmpty(texturesB64)) return null;

            var inner = Encoding.UTF8.GetString(Convert.FromBase64String(texturesB64));
            using var texturesDoc = JsonDocument.Parse(inner);
            if (!texturesDoc.RootElement.TryGetProperty("textures", out var textures) || textures.ValueKind != JsonValueKind.Object)
                return null;
            if (!textures.TryGetProperty("SKIN", out var skinNode) || skinNode.ValueKind != JsonValueKind.Object)
                return null;
            if (!skinNode.TryGetProperty("url", out var urlEl) || urlEl.ValueKind != JsonValueKind.String)
                return null;
            var pngUrl = urlEl.GetString();
            if (string.IsNullOrEmpty(pngUrl)) return null;

            var variant = SkinVariant.Classic;
            if (skinNode.TryGetProperty("metadata", out var meta)
                && meta.ValueKind == JsonValueKind.Object
                && meta.TryGetProperty("model", out var model)
                && model.ValueKind == JsonValueKind.String
                && string.Equals(model.GetString(), "slim", StringComparison.OrdinalIgnoreCase))
            {
                variant = SkinVariant.Slim;
            }

            // Canonical-name capitalisation from Mojang wins over the user's typed casing -
            // a player called "Notch" who searches "notch" still sees "Notch" on the card.
            return new BrowsedSkin(
                Id: "mojang:" + uuid,
                SourceUrl: NameMcProfileStem + uuid,
                ThumbnailUrl: pngUrl,
                PngDownloadUrl: pngUrl,
                Variant: variant,
                UploaderName: canonicalName,
                Tags: Array.Empty<string>(),
                Likes: 0,
                IsLivePlayerSkin: true);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException) { return null; }
        catch (JsonException) { return null; }
        catch (FormatException) { return null; } // bad base64
        catch (DecoderFallbackException) { return null; } // bad UTF-8 in the b64 payload
    }

    /// <inheritdoc />
    public async Task<byte[]> DownloadPngAsync(BrowsedSkin skin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(skin);
        if (string.IsNullOrWhiteSpace(skin.PngDownloadUrl))
            throw new ArgumentException("BrowsedSkin.PngDownloadUrl is empty.", nameof(skin));

        using var response = await _http
            .GetAsync(skin.PngDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Parse a MineSkin v2 <c>/v2/skins</c> JSON response into <see cref="BrowsedSkin"/> records.
    /// Public so tests can exercise the parser from a JSON string without HTTP.
    /// </summary>
    /// <param name="json">Raw JSON returned by the list endpoint.</param>
    /// <param name="limit">Maximum number of skins to return; non-positive means "no cap".</param>
    /// <param name="query">
    /// Optional case-insensitive substring filter applied to each entry's <c>name</c> and
    /// <c>shortId</c>. <c>null</c> means "no filter".
    /// </param>
    /// <remarks>
    /// Failures inside a single entry (missing texture hash, malformed JSON shape) are
    /// silently skipped so a partial response still yields the rest of the feed.
    /// </remarks>
    public static IReadOnlyList<BrowsedSkin> ParseList(string json, int limit, string? query)
    {
        if (string.IsNullOrEmpty(json)) return Array.Empty<BrowsedSkin>();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return Array.Empty<BrowsedSkin>();
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("skins", out var skinsEl) ||
                skinsEl.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<BrowsedSkin>();
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<BrowsedSkin>();

            foreach (var entry in skinsEl.EnumerateArray())
            {
                if (limit > 0 && results.Count >= limit) break;
                var parsed = TryParseEntry(entry, query);
                if (parsed is null) continue;
                if (!seen.Add(parsed.Id)) continue;
                results.Add(parsed);
            }

            return results;
        }
    }

    /// <summary>
    /// Try to map a single JSON entry from <c>skins[]</c> to a <see cref="BrowsedSkin"/>.
    /// Returns null when the entry is missing the texture hash (the only field we can't
    /// fall back on) or fails the optional <paramref name="query"/> filter.
    /// </summary>
    public static BrowsedSkin? TryParseEntry(JsonElement entry, string? query)
    {
        if (entry.ValueKind != JsonValueKind.Object) return null;

        var texture = GetStringOrNull(entry, "texture");
        if (string.IsNullOrEmpty(texture)) return null;

        var uuid = GetStringOrNull(entry, "uuid") ?? texture;
        var shortId = GetStringOrNull(entry, "shortId") ?? string.Empty;
        var name = GetStringOrNull(entry, "name");

        // Client-side filter: substring match against name + shortId. Case-insensitive.
        if (!string.IsNullOrEmpty(query))
        {
            var match = (name is not null && name.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(shortId) && shortId.Contains(query, StringComparison.OrdinalIgnoreCase));
            if (!match) return null;
        }

        var sourceUrl = string.IsNullOrEmpty(shortId)
            ? $"{WebPageStem}{uuid}"
            : $"{WebPageStem}{shortId}";

        // MineSkin's anonymous feed gives no thumbnail URL, so we point the gallery image
        // directly at the raw 64x64 PNG on textures.minecraft.net. Avalonia's AsyncImageLoader
        // scales it up with bitmap interpolation off (set on the consuming Image), giving
        // the pixel-art look the rest of the skins UI already uses.
        var pngUrl = TextureCdn + texture;
        var thumbnail = pngUrl;

        // Use the skin name when present; otherwise fall back to the shortId so the user
        // has *something* to identify the card by in the selected-card panel.
        var uploader = !string.IsNullOrWhiteSpace(name)
            ? name
            : (!string.IsNullOrEmpty(shortId) ? shortId : null);

        return new BrowsedSkin(
            Id: uuid,
            SourceUrl: sourceUrl,
            ThumbnailUrl: thumbnail,
            PngDownloadUrl: pngUrl,
            Variant: SkinVariant.Classic,
            UploaderName: uploader,
            Tags: Array.Empty<string>(),
            Likes: 0);
    }

    private static string? GetStringOrNull(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Null || el.ValueKind == JsonValueKind.Undefined) return null;
        if (el.ValueKind != JsonValueKind.String) return null;
        var s = el.GetString();
        return string.IsNullOrEmpty(s) ? null : s;
    }
}
