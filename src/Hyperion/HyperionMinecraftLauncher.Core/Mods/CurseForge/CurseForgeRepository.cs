using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Installations;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Mods.CurseForge;

/// <summary>
/// <see cref="IModRepository"/> talking to the CurseForge v1 REST API
/// (<c>https://api.curseforge.com/v1</c>). Unlike Modrinth, CurseForge requires every
/// developer to obtain their own API key from <c>console.curseforge.com</c> - we never
/// ship one. When the configured key is empty the launcher gracefully disables the
/// integration: <see cref="SearchAsync"/> returns an empty list (and warns once),
/// while <see cref="ListFilesAsync"/> / <see cref="DownloadAsync"/> throw
/// <see cref="NotSupportedException"/> so the UI can show a "configure your key" hint.
/// </summary>
public sealed class CurseForgeRepository : IModRepository
{
    /// <summary>CurseForge's game id for Minecraft, used as the <c>gameId</c> query param.</summary>
    public const int MinecraftGameId = 432;

    /// <summary>Base URL for the CurseForge v1 REST API.</summary>
    public const string DefaultBaseAddress = "https://api.curseforge.com/v1/";

    private const string KeyMissingMessage = "CurseForge API key not configured";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly Func<string> _apiKeyProvider;
    private readonly ILauncherLogger _logger;
    private bool _keyWarningEmitted;

    /// <summary>
    /// Construct the repository with an injected <see cref="HttpClient"/>. The base address
    /// is set to <see cref="DefaultBaseAddress"/> when missing. An empty <paramref name="apiKey"/>
    /// disables the network integration; the constructor itself never throws.
    /// </summary>
    /// <remarks>
    /// This overload snapshots the key at construction time. New callers should prefer the
    /// <see cref="CurseForgeRepository(HttpClient, Func{string}, ILauncherLogger)"/> overload
    /// so the live key from <c>LauncherSettings</c> is picked up on each call without
    /// rebuilding the repository (otherwise the user has to restart the launcher after
    /// pasting a key into Settings - the v0.32.1 onboarding bug).
    /// </remarks>
    public CurseForgeRepository(HttpClient http, string apiKey, ILauncherLogger logger)
        : this(http, () => apiKey ?? string.Empty, logger)
    {
    }

    /// <summary>
    /// Construct the repository with an injected <see cref="HttpClient"/> and a delegate that
    /// resolves the current API key on every request. This is the live-settings variant used
    /// by the App so the user can paste a key into the onboarding dialog and have the next
    /// search hit CurseForge without restarting the launcher.
    /// </summary>
    public CurseForgeRepository(HttpClient http, Func<string> apiKeyProvider, ILauncherLogger logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _apiKeyProvider = apiKeyProvider ?? throw new ArgumentNullException(nameof(apiKeyProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(DefaultBaseAddress);

        // v0.32.2: defensive scrub - some callers share a HttpClient across services and a
        // stale default `x-api-key` header from a previous build of the launcher would
        // shadow the per-request value silently. NewRequest below is the only writer.
        _http.DefaultRequestHeaders.Remove("x-api-key");
        _http.DefaultRequestHeaders.Remove("X-API-Key");
    }

    /// <summary>
    /// Resolve the current API key via the configured provider. Empty / null is normalised
    /// to <see cref="string.Empty"/> so call-sites can check via <see cref="string.IsNullOrEmpty"/>.
    /// </summary>
    private string CurrentApiKey()
    {
        try { return _apiKeyProvider() ?? string.Empty; }
        catch { return string.Empty; }
    }

    /// <inheritdoc />
    public ModSource Source => ModSource.CurseForge;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Mod>> SearchAsync(ModSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var apiKey = CurrentApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            if (!_keyWarningEmitted)
            {
                _logger.Warn(KeyMissingMessage + " - paste a key into Settings to enable CurseForge search.");
                _keyWarningEmitted = true;
            }
            return Array.Empty<Mod>();
        }
        // The user pasted a fresh key after the last "no key" warning - reset the latch so a
        // future unset+set cycle gets its own warning line in the log.
        _keyWarningEmitted = false;

        var sb = new StringBuilder("mods/search?gameId=").Append(MinecraftGameId);
        if (!string.IsNullOrEmpty(query.Query))
            sb.Append("&searchFilter=").Append(Uri.EscapeDataString(query.Query));
        if (!string.IsNullOrEmpty(query.GameVersion))
            sb.Append("&gameVersion=").Append(Uri.EscapeDataString(query.GameVersion));
        if (query.Loader is { } loader && TryLoaderType(loader, out var loaderType))
            sb.Append("&modLoaderType=").Append(loaderType);
        sb.Append("&pageSize=").Append(query.Limit);
        sb.Append("&index=").Append(query.Offset);

        var relativeUrl = sb.ToString();
        LogOutgoing(relativeUrl, apiKey);
        using var request = NewRequest(HttpMethod.Get, relativeUrl, apiKey);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessOrFriendlyAsync(response, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return ParseSearchHits(stream);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModFile>> ListFilesAsync(string modId, string? gameVersion, ModLoader? loader, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);

        var apiKey = CurrentApiKey();
        if (string.IsNullOrEmpty(apiKey))
            throw new NotSupportedException(KeyMissingMessage);

        var sb = new StringBuilder("mods/").Append(Uri.EscapeDataString(modId)).Append("/files?");
        var first = true;
        if (!string.IsNullOrEmpty(gameVersion))
        {
            sb.Append("gameVersion=").Append(Uri.EscapeDataString(gameVersion));
            first = false;
        }
        if (loader is { } l && TryLoaderType(l, out var loaderType))
        {
            if (!first) sb.Append('&');
            sb.Append("modLoaderType=").Append(loaderType);
        }

        var relativeUrl = sb.ToString();
        LogOutgoing(relativeUrl, apiKey);
        using var request = NewRequest(HttpMethod.Get, relativeUrl, apiKey);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessOrFriendlyAsync(response, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return ParseFiles(stream, modId);
    }

    /// <inheritdoc />
    public async Task DownloadAsync(ModFile file, Stream destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(destination);

        var apiKey = CurrentApiKey();
        if (string.IsNullOrEmpty(apiKey))
            throw new NotSupportedException(KeyMissingMessage);

        // v0.32.2: route through NewRequest so all three verbs (search / list-files / download)
        // share the same header-construction code path. The download URL is absolute, so
        // HttpRequestMessage accepts it without consulting HttpClient.BaseAddress.
        LogOutgoing(file.DownloadUrl, apiKey);
        using var request = NewRequest(HttpMethod.Get, file.DownloadUrl, apiKey);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessOrFriendlyAsync(response, cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// Construct an <see cref="HttpRequestMessage"/> carrying the freshly resolved API key
    /// in its own <c>x-api-key</c> header (lower-case per the CurseForge docs). Building the
    /// message per-call is what lets the user paste a new key into onboarding and have the
    /// very next search authenticate correctly - <see cref="HttpClient.DefaultRequestHeaders"/>
    /// is intentionally NOT touched, so a stale key cannot leak between sessions.
    /// </summary>
    private HttpRequestMessage NewRequest(HttpMethod method, string url, string apiKey)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        return req;
    }

    /// <summary>
    /// Emit a diagnostic <c>[mods] GET ...</c> line so future 403 / 5xx investigations can
    /// happen from the log file alone. The url is logged verbatim (no api key is in the URL -
    /// CurseForge takes it via the <c>x-api-key</c> header), and the key itself is reduced
    /// to its length to avoid leaking secrets into long-lived support logs.
    /// </summary>
    private void LogOutgoing(string url, string apiKey)
    {
        var absolute = TryBuildAbsoluteUrl(url);
        _logger.Info($"[mods] GET {absolute} (key length: {apiKey.Length})");
    }

    private string TryBuildAbsoluteUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs))
            return abs.ToString();
        if (_http.BaseAddress is { } baseAddr && Uri.TryCreate(baseAddr, url, out var combined))
            return combined.ToString();
        return url;
    }

    /// <summary>
    /// Translate non-success responses into <see cref="HttpRequestException"/>s whose message
    /// is the friendly hint the user actually needs - "check your key", "service is down",
    /// etc. The caller's <c>catch (Exception ex)</c> surfaces <c>ex.Message</c> straight into
    /// the launcher log so the user sees a sentence instead of <c>"403 (Forbidden)"</c>.
    /// </summary>
    private static async Task EnsureSuccessOrFriendlyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var status = response.StatusCode;
        var hint = status switch
        {
            HttpStatusCode.Forbidden =>
                "CurseForge rejected the request. Check the API key in Settings is valid and has access to game id 432 (Minecraft).",
            HttpStatusCode.Unauthorized =>
                "CurseForge says the API key is invalid or expired.",
            _ when (int)status >= 500 =>
                "CurseForge service is unavailable, try again later.",
            HttpStatusCode.TooManyRequests =>
                "CurseForge is rate-limiting the launcher, slow down and try again in a minute.",
            HttpStatusCode.NotFound =>
                "CurseForge endpoint not found - verify the launcher build is current.",
            _ =>
                $"CurseForge returned {(int)status} {status}.",
        };

        // Best-effort body sniff for the log; ignored on stream errors. Truncated so a giant
        // HTML error page doesn't drown the launcher log.
        string body;
        try { body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
        catch { body = string.Empty; }
        if (body.Length > 300) body = body[..300] + "...";

        throw new HttpRequestException(string.IsNullOrEmpty(body) ? hint : hint + " (" + body + ")");
    }

    /// <summary>CurseForge's numeric loader-type identifiers.</summary>
    /// <remarks>1=Forge, 4=Fabric, 5=Quilt, 6=NeoForge per their API docs.</remarks>
    private static bool TryLoaderType(ModLoader loader, out int code)
    {
        switch (loader)
        {
            case ModLoader.Forge: code = 1; return true;
            case ModLoader.Fabric: code = 4; return true;
            case ModLoader.Quilt: code = 5; return true;
            case ModLoader.NeoForge: code = 6; return true;
            case ModLoader.LegacyForge: code = 1; return true;
            default: code = 0; return false;
        }
    }

    /// <summary>Parse a CurseForge <c>/mods/search</c> JSON payload. Public for direct unit tests.</summary>
    public static IReadOnlyList<Mod> ParseSearchHits(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return Array.Empty<Mod>();

        var list = new List<Mod>(data.GetArrayLength());
        foreach (var el in data.EnumerateArray())
        {
            var id = ReadInt64(el, "id").ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (id == "0") continue;

            var iconUri = el.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.Object
                ? ReadString(logo, "url")
                : null;
            var pageUri = el.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Object
                ? ReadString(links, "websiteUrl")
                : null;
            var author = string.Empty;
            if (el.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array)
                foreach (var a in authors.EnumerateArray())
                {
                    var name = ReadString(a, "name");
                    if (!string.IsNullOrEmpty(name)) { author = name; break; }
                }

            var categories = new List<string>();
            if (el.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
                foreach (var c in cats.EnumerateArray())
                {
                    var name = ReadString(c, "name");
                    if (!string.IsNullOrEmpty(name)) categories.Add(name);
                }

            list.Add(new Mod
            {
                Id = id,
                Slug = ReadString(el, "slug") ?? id,
                Name = ReadString(el, "name") ?? id,
                Description = ReadString(el, "summary") ?? string.Empty,
                AuthorDisplay = author,
                IconUri = iconUri,
                PageUri = pageUri,
                Categories = categories,
                Downloads = ReadInt64(el, "downloadCount"),
                Source = ModSource.CurseForge,
            });
        }
        return list;
    }

    /// <summary>Parse a CurseForge <c>/mods/{id}/files</c> JSON payload. Public for direct unit tests.</summary>
    public static IReadOnlyList<ModFile> ParseFiles(Stream stream, string modId)
    {
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return Array.Empty<ModFile>();

        var list = new List<ModFile>(data.GetArrayLength());
        foreach (var f in data.EnumerateArray())
        {
            var fileId = ReadInt64(f, "id").ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (fileId == "0") continue;
            var url = ReadString(f, "downloadUrl") ?? string.Empty;
            if (string.IsNullOrEmpty(url)) continue;

            var gameVersions = new List<string>();
            if (f.TryGetProperty("gameVersions", out var gvs) && gvs.ValueKind == JsonValueKind.Array)
                foreach (var v in gvs.EnumerateArray())
                    if (v.ValueKind == JsonValueKind.String)
                        gameVersions.Add(v.GetString() ?? string.Empty);

            string? sha1 = null;
            if (f.TryGetProperty("hashes", out var hashes) && hashes.ValueKind == JsonValueKind.Array)
            {
                foreach (var h in hashes.EnumerateArray())
                {
                    // CurseForge encodes hash algorithm as a numeric code: 1=Sha1, 2=Md5.
                    var algo = ReadInt64(h, "algo");
                    if (algo == 1)
                    {
                        sha1 = ReadString(h, "value");
                        break;
                    }
                }
            }

            list.Add(new ModFile
            {
                ModId = modId,
                FileId = fileId,
                DisplayName = ReadString(f, "displayName") ?? fileId,
                Filename = ReadString(f, "fileName"),
                DownloadUrl = url,
                GameVersions = gameVersions,
                Loaders = Array.Empty<ModLoader>(),
                FileSize = ReadInt64(f, "fileLength"),
                Sha1 = sha1,
            });
        }
        return list;
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
}
