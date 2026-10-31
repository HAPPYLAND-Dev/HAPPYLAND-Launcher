using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Cache;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Updates;

/// <summary>
/// Polls the GitHub Releases REST endpoint for the launcher repo and reports any
/// <see cref="UpdateInfo"/> strictly newer than the currently running build. Cache-first
/// (1 h TTL) so repeated launcher startups don't hammer api.github.com. Returns <c>null</c>
/// on every failure path - schema drift, 404, transport error - so a broken update
/// check can never block launcher startup.
/// </summary>
public sealed class GitHubReleasesUpdateChecker : IUpdateChecker
{
    /// <summary>Public for tests / advanced wiring; production callers should not need to override.</summary>
    public const string DefaultEndpoint = "https://api.github.com/repos/TechTeaStudio/HyperionMinecraftLauncher/releases/latest";

    private const string CacheKey = "updates/latest.json";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly HttpClient _http;
    private readonly FileCache? _cache;
    private readonly string _endpoint;

    /// <summary>Default constructor: builds its own HttpClient, hits the GitHub endpoint, no disk cache.</summary>
    public GitHubReleasesUpdateChecker() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }) { }

    /// <summary>Production / test constructor.</summary>
    public GitHubReleasesUpdateChecker(HttpClient http, FileCache? cache = null, string? endpoint = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _cache = cache;
        _endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint;
    }

    /// <inheritdoc />
    public async Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentVersion))
            return null;

        byte[]? bytes = null;

        // 1. Cache hit (fresh).
        if (_cache is not null)
        {
            try
            {
                bytes = await _cache.TryReadAsync(CacheKey, CacheTtl, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cache read; fall through to network.
                bytes = null;
            }
        }

        // 2. Network fetch.
        if (bytes is null || bytes.Length == 0)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
                // GitHub rejects requests without a UA. Per their docs, anything stable is fine.
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("HyperionMinecraftLauncher", currentVersion));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;

                bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

                if (_cache is not null && bytes is { Length: > 0 })
                {
                    try { await _cache.WriteAsync(CacheKey, bytes, cancellationToken).ConfigureAwait(false); }
                    catch { /* best effort */ }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        if (bytes is null || bytes.Length == 0)
            return null;

        UpdateInfo? parsed;
        try
        {
            parsed = ParseLatestRelease(bytes);
        }
        catch
        {
            return null;
        }

        if (parsed is null)
            return null;

        return SemverComparer.IsNewerThan(parsed.LatestVersion, currentVersion) ? parsed : null;
    }

    /// <summary>
    /// Parse a single GitHub release JSON object into an <see cref="UpdateInfo"/>. Returns
    /// <c>null</c> when the required <c>tag_name</c> is missing or empty. Public for tests.
    /// </summary>
    public static UpdateInfo? ParseLatestRelease(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;

        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        var rawTag = ReadString(root, "tag_name");
        if (string.IsNullOrWhiteSpace(rawTag)) return null;

        // Strip a leading "v" so consumers compare bare X.Y.Z numbers.
        var latest = rawTag.Trim();
        if (latest.Length > 0 && (latest[0] == 'v' || latest[0] == 'V'))
            latest = latest.Substring(1);

        var url = ReadString(root, "html_url") ?? string.Empty;
        var body = ReadString(root, "body") ?? string.Empty;

        DateTimeOffset published = default;
        var rawDate = ReadString(root, "published_at");
        if (!string.IsNullOrEmpty(rawDate))
        {
            if (DateTimeOffset.TryParse(rawDate, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var dt))
            {
                published = dt;
            }
        }

        return new UpdateInfo(latest, url, published, body);
    }

    private static string? ReadString(JsonElement el, string property)
        => el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
