using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Browser;

/// <summary>
/// <see cref="ISkinBrowser"/> backed by public pages on <c>https://namemc.com</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Deprecated in v0.32.3.</strong> NameMC's Cloudflare layer now issues a
/// JavaScript challenge (<c>cf-mitigated=challenge</c>) to every scripted request -
/// pure HTTP cannot bypass it without a headless browser. The class is kept here for
/// reference and so the parser-shape tests still execute, but the live launcher
/// wires <see cref="MineSkinBrowser"/> instead. See <c>App.axaml.cs</c> for the DI swap.
/// </para>
/// <para>
/// NameMC publishes <strong>no official API</strong>. This implementation parses
/// the HTML of the public trending and search pages with HtmlAgilityPack and is
/// best-effort: structural changes upstream can require selector tweaks. A failure
/// to parse a single card is swallowed; only a wholesale fetch failure surfaces
/// as an exception.
/// </para>
/// <para>
/// <strong>Bot detection.</strong> NameMC (and the Cloudflare layer in front of it)
/// actively block scripted access whose fingerprint differs from a real browser:
/// a non-browser User-Agent, missing <c>Accept-Language</c>, no <c>Sec-Fetch-*</c>,
/// or no <c>Accept-Encoding</c> all trigger 403. We mitigate by setting the full
/// Chrome 124 header set in <see cref="ConfigureHttpClient(HttpClient)"/>. Even so,
/// the integration is best-effort: a 403 still surfaces and is logged so the user
/// knows the gallery is temporarily unavailable.
/// </para>
/// <para>
/// The skin "hash" appears in every card href as <c>/skin/{hash}</c>. Combined with
/// the documented NameMC asset URLs:
/// <list type="bullet">
///   <item><description><c>https://textures.minecraft.net/texture/{hash}</c> -
///   raw 64x64 PNG (the same CDN every Mojang account ships its active skin from).</description></item>
///   <item><description><c>https://s.namemc.com/3d/skin/body.png?id={hash}&amp;model={classic|slim}</c> -
///   pre-rendered body thumbnail (used as the gallery image).</description></item>
/// </list>
/// </para>
/// </remarks>
[Obsolete("NameMC blocks scripted access via Cloudflare; use MineSkinBrowser instead.")]
public sealed class NameMcSkinBrowser : ISkinBrowser
{
    /// <summary>Public landing-page URL the parser targets for "Show trending".</summary>
    public const string TrendingUrl = "https://namemc.com/minecraft-skins/trending/top";

    /// <summary>Public search URL stem; the query is appended via <c>?q=...</c>.</summary>
    public const string SearchUrlStem = "https://namemc.com/minecraft-skins";

    /// <summary>
    /// User-Agent the browser identifies as. A real Chrome-on-Windows 10 UA: the
    /// previous launcher-branded UA was rejected by Cloudflare with 403. Bump the
    /// Chrome major version every couple of months so the fingerprint stays current.
    /// </summary>
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    /// <summary>The <c>Accept</c> header Chrome sends on a top-level document navigation.</summary>
    public const string AcceptHeader =
        "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8";

    /// <summary>The <c>Accept-Language</c> header. Plain en-US; NameMC has no localized variants.</summary>
    public const string AcceptLanguageHeader = "en-US,en;q=0.5";

    /// <summary>The <c>Accept-Encoding</c> header advertising gzip/deflate/brotli.</summary>
    /// <remarks>
    /// The HttpClient's underlying handler must also have
    /// <c>AutomaticDecompression = GZip | Deflate | Brotli</c> set so the response
    /// body is transparently decompressed. <see cref="ConfigureHttpClient(HttpClient)"/>
    /// only sets the advertise header; the handler-level flag is the App's responsibility.
    /// </remarks>
    public const string AcceptEncodingHeader = "gzip, deflate, br";

    /// <summary>The <c>Referer</c> header. NameMC's bot wall checks for the site's own origin.</summary>
    public const string RefererHeader = "https://namemc.com/";

    /// <summary>Direct CDN for raw skin PNGs (the same one Mojang's profile URLs serve from).</summary>
    public const string TextureCdn = "https://textures.minecraft.net/texture/";

    /// <summary>Pre-rendered body-thumbnail CDN. The model query param doubles as the variant hint.</summary>
    public const string ThumbCdn = "https://s.namemc.com/3d/skin/body.png";

    private readonly HttpClient _http;

    /// <summary>Construct against an arbitrary <see cref="HttpClient"/>.</summary>
    /// <remarks>
    /// The constructor calls <see cref="ConfigureHttpClient(HttpClient)"/> so every
    /// outbound request carries the Chrome-shaped header set. Tests pass in a client
    /// wired to a mock <c>HttpMessageHandler</c> so the parser can be exercised
    /// without network.
    /// </remarks>
    public NameMcSkinBrowser(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        ConfigureHttpClient(_http);
    }

    /// <summary>
    /// Apply the full browser-like header set to <paramref name="http"/>. Idempotent:
    /// existing values for any of these headers are preserved (so the App can
    /// pre-stamp them before passing the client in, and the constructor's call is a
    /// no-op).
    /// </summary>
    /// <remarks>
    /// Sets: User-Agent, Accept, Accept-Language, Accept-Encoding, Referer,
    /// Upgrade-Insecure-Requests, the four Sec-Fetch-* hints Chrome sends on a
    /// top-level document navigation, and Cache-Control / Pragma no-cache so
    /// Cloudflare's edge doesn't serve us a stale anti-bot page. Also pins
    /// <see cref="HttpClient.DefaultRequestVersion"/> to HTTP/2 - real browsers
    /// negotiate h2 on namemc.com and the bot wall flags HTTP/1.1.
    /// </remarks>
    public static void ConfigureHttpClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);

        var h = http.DefaultRequestHeaders;
        if (!h.UserAgent.Any())
        {
            if (!h.UserAgent.TryParseAdd(UserAgent))
                h.TryAddWithoutValidation("User-Agent", UserAgent);
        }
        if (!h.Accept.Any()) h.TryAddWithoutValidation("Accept", AcceptHeader);
        if (!h.AcceptLanguage.Any()) h.TryAddWithoutValidation("Accept-Language", AcceptLanguageHeader);
        if (!h.AcceptEncoding.Any()) h.TryAddWithoutValidation("Accept-Encoding", AcceptEncodingHeader);
        if (!h.Contains("Referer")) h.TryAddWithoutValidation("Referer", RefererHeader);
        if (!h.Contains("Upgrade-Insecure-Requests")) h.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        if (!h.Contains("Sec-Fetch-Dest")) h.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        if (!h.Contains("Sec-Fetch-Mode")) h.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        if (!h.Contains("Sec-Fetch-Site")) h.TryAddWithoutValidation("Sec-Fetch-Site", "none");
        if (!h.Contains("Sec-Fetch-User")) h.TryAddWithoutValidation("Sec-Fetch-User", "?1");
        if (!h.Contains("Cache-Control")) h.TryAddWithoutValidation("Cache-Control", "no-cache");
        if (!h.Contains("Pragma")) h.TryAddWithoutValidation("Pragma", "no-cache");

        // Real browsers negotiate HTTP/2 with NameMC; some Cloudflare rules flag
        // HTTP/1.1 from non-mobile UAs. Setting Version20 + RequestVersionOrLower so
        // we still fall back if the server doesn't speak h2.
        http.DefaultRequestVersion = HttpVersion.Version20;
        http.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrowsedSkin>> ListTrendingAsync(int limit, CancellationToken cancellationToken)
    {
        var html = await GetHtmlAsync(TrendingUrl, cancellationToken).ConfigureAwait(false);
        return ParseGallery(html, limit);
    }

    /// <inheritdoc />
    /// <remarks>
    /// NameMC's trending page is not paginated through a query parameter the way MineSkin v2 is,
    /// so this implementation returns an empty list for any <paramref name="page"/> &gt; 0 and
    /// delegates to <see cref="ListTrendingAsync(int, CancellationToken)"/> for page 0.
    /// </remarks>
    public async Task<IReadOnlyList<BrowsedSkin>> ListTrendingAsync(int page, int limit, CancellationToken cancellationToken)
    {
        if (page > 0) return Array.Empty<BrowsedSkin>();
        return await ListTrendingAsync(limit, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrowsedSkin>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var q = query ?? string.Empty;
        var url = string.IsNullOrWhiteSpace(q)
            ? TrendingUrl
            : SearchUrlStem + "?q=" + Uri.EscapeDataString(q);
        var html = await GetHtmlAsync(url, cancellationToken).ConfigureAwait(false);
        return ParseGallery(html, limit);
    }

    /// <inheritdoc />
    /// <remarks>NameMC's search URL is not paginated; page &gt; 0 returns empty.</remarks>
    public async Task<IReadOnlyList<BrowsedSkin>> SearchAsync(string query, int page, int limit, CancellationToken cancellationToken)
    {
        if (page > 0) return Array.Empty<BrowsedSkin>();
        return await SearchAsync(query, limit, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<byte[]> DownloadPngAsync(BrowsedSkin skin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(skin);
        var url = string.IsNullOrWhiteSpace(skin.PngDownloadUrl)
            ? throw new ArgumentException("BrowsedSkin.PngDownloadUrl is empty.", nameof(skin))
            : skin.PngDownloadUrl;
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetHtmlAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Best-effort diagnostics: when NameMC's anti-bot wall fires, the response
            // headers (Server, CF-Ray, Set-Cookie) are the clearest signal. Embed them
            // in the thrown exception's message so the caller's log catches it without
            // needing extra plumbing.
            var diag = new StringBuilder(256);
            diag.Append("NameMC ").Append(url).Append(" -> ").Append((int)response.StatusCode)
                .Append(' ').Append(response.ReasonPhrase ?? string.Empty);
            foreach (var hh in response.Headers)
            {
                if (hh.Key.StartsWith("CF-", StringComparison.OrdinalIgnoreCase) ||
                    hh.Key.Equals("Server", StringComparison.OrdinalIgnoreCase))
                {
                    diag.Append("; ").Append(hh.Key).Append('=').Append(string.Join(',', hh.Value));
                }
            }
            throw new HttpRequestException(diag.ToString(), inner: null, statusCode: response.StatusCode);
        }
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Parse a NameMC gallery HTML page into a (capped) list of <see cref="BrowsedSkin"/>.
    /// Public so the parser tests can exercise it from a string fixture without HTTP.
    /// </summary>
    /// <param name="html">Raw HTML returned by the trending / search endpoint.</param>
    /// <param name="limit">Maximum number of skins to return; non-positive means "no cap".</param>
    /// <remarks>
    /// Failures inside a single card (missing href, malformed hash, etc.) are silently
    /// skipped so a partially-broken page still yields the rest of the gallery.
    /// </remarks>
    public static IReadOnlyList<BrowsedSkin> ParseGallery(string html, int limit)
    {
        if (string.IsNullOrEmpty(html)) return Array.Empty<BrowsedSkin>();

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Pick anchor nodes whose href starts with "/skin/" - the canonical card link.
        // We deliberately don't bind to a wrapper class name; the parent .card / .skin-card
        // class strings are the most fragile bits of the NameMC markup.
        var anchors = doc.DocumentNode.SelectNodes("//a[contains(@href, '/skin/')]");
        if (anchors is null) return Array.Empty<BrowsedSkin>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<BrowsedSkin>();

        foreach (var a in anchors)
        {
            if (limit > 0 && results.Count >= limit) break;

            BrowsedSkin? parsed;
            try
            {
                parsed = TryParseCard(a);
            }
            catch
            {
                // Defensive: any one card going wrong should not poison the page.
                parsed = null;
            }

            if (parsed is null) continue;
            if (!seen.Add(parsed.Id)) continue; // de-dupe: trending pages often repeat the same card across grid breakpoints
            results.Add(parsed);
        }

        return results;
    }

    /// <summary>
    /// Parse one card anchor. Returns null when the href doesn't carry a valid hash
    /// (the card belongs to some other section, e.g. a category teaser).
    /// </summary>
    private static BrowsedSkin? TryParseCard(HtmlNode anchor)
    {
        var href = anchor.GetAttributeValue("href", null);
        if (string.IsNullOrEmpty(href)) return null;

        var hash = ExtractHashFromHref(href);
        if (hash is null) return null;

        // Walk up the DOM to find the enclosing card so we can pick uploader / likes /
        // tags from sibling nodes. The card wrapper varies (article, div), so we cap
        // the walk distance and accept either.
        var card = ClosestCard(anchor);

        var sourceUrl = AbsoluteSourceUrl(href);
        var variant = DetectVariant(anchor, card);
        var thumbnail = DetectThumbnail(anchor, hash, variant);

        var uploader = DetectUploader(card);
        var tags = DetectTags(card);
        var likes = DetectLikes(card);

        return new BrowsedSkin(
            Id: hash,
            SourceUrl: sourceUrl,
            ThumbnailUrl: thumbnail,
            PngDownloadUrl: TextureCdn + hash,
            Variant: variant,
            UploaderName: uploader,
            Tags: tags,
            Likes: likes);
    }

    /// <summary>
    /// Pull the skin hash out of a NameMC card href. NameMC URLs come in two shapes:
    /// <list type="bullet">
    ///   <item><description><c>/skin/{hash}</c> - canonical link</description></item>
    ///   <item><description><c>https://namemc.com/skin/{hash}</c> - absolute</description></item>
    /// </list>
    /// Returns null when no hash is present.
    /// </summary>
    public static string? ExtractHashFromHref(string href)
    {
        if (string.IsNullOrEmpty(href)) return null;
        const string Marker = "/skin/";
        var idx = href.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var rest = href[(idx + Marker.Length)..];
        // Trim trailing slash / query / fragment.
        var cut = rest.IndexOfAny(new[] { '/', '?', '#' });
        if (cut >= 0) rest = rest[..cut];
        return rest.Length == 0 ? null : rest;
    }

    private static string AbsoluteSourceUrl(string href)
    {
        if (string.IsNullOrEmpty(href)) return "https://namemc.com/";
        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return href;
        if (href.StartsWith('/')) return "https://namemc.com" + href;
        return "https://namemc.com/" + href;
    }

    private static HtmlNode? ClosestCard(HtmlNode node)
    {
        var current = node.ParentNode;
        for (var i = 0; current is not null && i < 4; i++)
        {
            var cls = current.GetAttributeValue("class", string.Empty);
            if (!string.IsNullOrEmpty(cls) &&
                (cls.Contains("card", StringComparison.OrdinalIgnoreCase) ||
                 cls.Contains("skin", StringComparison.OrdinalIgnoreCase)))
            {
                return current;
            }
            current = current.ParentNode;
        }
        return node.ParentNode;
    }

    private static SkinVariant DetectVariant(HtmlNode anchor, HtmlNode? card)
    {
        // 1) Some cards mark the model on the anchor (data-model="slim").
        var dataModel = anchor.GetAttributeValue("data-model", null);
        if (IsSlimModelName(dataModel)) return SkinVariant.Slim;

        // 2) The body thumbnail <img> often carries either ?model=slim in its src or a
        //    data-model attribute on the <img> itself.
        var img = anchor.SelectSingleNode(".//img[@src]");
        if (img is not null)
        {
            var imgModel = img.GetAttributeValue("data-model", null);
            if (IsSlimModelName(imgModel)) return SkinVariant.Slim;

            var src = img.GetAttributeValue("src", string.Empty) ?? string.Empty;
            if (src.IndexOf("model=slim", StringComparison.OrdinalIgnoreCase) >= 0) return SkinVariant.Slim;
        }

        // 3) Card-level class hints (e.g. ".model-slim", ".slim").
        if (card is not null)
        {
            var cls = card.GetAttributeValue("class", string.Empty) ?? string.Empty;
            if (cls.IndexOf("slim", StringComparison.OrdinalIgnoreCase) >= 0) return SkinVariant.Slim;
        }

        return SkinVariant.Classic;
    }

    private static bool IsSlimModelName(string? value) =>
        !string.IsNullOrEmpty(value) &&
        (value.Equals("slim", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("alex", StringComparison.OrdinalIgnoreCase));

    private static string DetectThumbnail(HtmlNode anchor, string hash, SkinVariant variant)
    {
        // Prefer the page's own <img src=...> when present (it's already a cached body shot).
        var img = anchor.SelectSingleNode(".//img[@src]");
        if (img is not null)
        {
            var src = img.GetAttributeValue("src", null);
            if (!string.IsNullOrEmpty(src))
            {
                if (src.StartsWith("//")) return "https:" + src;
                if (src.StartsWith("/")) return "https://namemc.com" + src;
                return src;
            }
        }

        // Fallback: build the canonical NameMC body thumbnail.
        var model = variant == SkinVariant.Slim ? "slim" : "classic";
        return $"{ThumbCdn}?id={hash}&model={model}";
    }

    private static string? DetectUploader(HtmlNode? card)
    {
        if (card is null) return null;
        var u = card.SelectSingleNode(".//a[contains(@href, '/profile/') or contains(@href, '/user/')]");
        if (u is null) return null;
        var text = HtmlEntity.DeEntitize(u.InnerText ?? string.Empty).Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string[] DetectTags(HtmlNode? card)
    {
        if (card is null) return Array.Empty<string>();
        var tags = card.SelectNodes(".//a[contains(@href, '/tag/')] | .//span[contains(@class, 'tag')]");
        if (tags is null || tags.Count == 0) return Array.Empty<string>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var t in tags)
        {
            var text = HtmlEntity.DeEntitize(t.InnerText ?? string.Empty).Trim().Trim('#');
            if (string.IsNullOrEmpty(text)) continue;
            if (seen.Add(text)) list.Add(text);
        }
        return list.ToArray();
    }

    private static int DetectLikes(HtmlNode? card)
    {
        if (card is null) return 0;
        // NameMC's like counters typically live in spans with classes like "card-text" or
        // "text-muted" next to a heart icon. Pull any nested element with data-likes / data-count
        // first, then fall back to scraping numbers next to a "heart" / "like" class anchor.
        var marked = card.SelectSingleNode(".//*[@data-count or @data-likes or @data-heart]");
        if (marked is not null)
        {
            var raw = marked.GetAttributeValue("data-count", null)
                     ?? marked.GetAttributeValue("data-likes", null)
                     ?? marked.GetAttributeValue("data-heart", null);
            if (TryParseCount(raw, out var n)) return n;
        }

        var heart = card.SelectSingleNode(".//*[contains(@class, 'heart') or contains(@class, 'like')]");
        if (heart is not null && TryParseCount(heart.InnerText, out var n2)) return n2;
        return 0;
    }

    private static bool TryParseCount(string? raw, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var trimmed = raw.Trim();
        // Strip thousands separators / non-digit decoration.
        var sb = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
            if (char.IsDigit(ch)) sb.Append(ch);
        if (sb.Length == 0) return false;
        return int.TryParse(sb.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
