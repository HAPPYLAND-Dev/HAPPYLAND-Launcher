using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;

/// <summary>
/// Default <see cref="IHeadlessServerJarFetcher"/> that goes through Mojang's public manifest
/// chain: <c>https://launchermeta.mojang.com/mc/game/version_manifest_v2.json</c> -> the entry
/// for the requested version id -> the per-version <c>{version}.json</c> at
/// <c>https://piston-meta.mojang.com/v1/packages/{hash}/{version}.json</c> -> the
/// <c>downloads.server</c> block (url + sha1 + size). The downloaded jar is written to
/// <c>{targetDirectory}/server.jar</c> and sha1-verified before the method returns.
/// </summary>
/// <remarks>
/// The fetcher writes a tiny <c>server.jar.sha1</c> sidecar next to the jar so a subsequent
/// EnsureServerJarAsync call can short-circuit when both the file and its recorded hash match
/// the manifest, avoiding the recompute-sha1-on-every-launch tax on big jars (a vanilla
/// server.jar is around 45 MB, so a fresh hash takes ~250 ms on a typical SSD).
/// </remarks>
public sealed class MojangServerJarFetcher : IHeadlessServerJarFetcher
{
    /// <summary>Versions index endpoint (same one Mojang's own launcher uses).</summary>
    public const string DefaultManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";

    private const string ServerJarFileName = "server.jar";
    private const string ServerJarHashFileName = "server.jar.sha1";

    private readonly HttpClient _http;
    private readonly string _manifestUrl;

    /// <summary>Default constructor builds its own short-timeout HttpClient.</summary>
    public MojangServerJarFetcher() : this(new HttpClient { Timeout = TimeSpan.FromMinutes(5) }) { }

    /// <summary>Production / test constructor. Manifest url override lets tests swap in a stub server.</summary>
    public MojangServerJarFetcher(HttpClient http, string? manifestUrl = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _manifestUrl = string.IsNullOrWhiteSpace(manifestUrl) ? DefaultManifestUrl : manifestUrl;
    }

    /// <inheritdoc />
    public async Task<string> EnsureServerJarAsync(
        string minecraftVersion,
        string targetDirectory,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        Directory.CreateDirectory(targetDirectory);
        var jarPath = Path.Combine(targetDirectory, ServerJarFileName);
        var hashPath = Path.Combine(targetDirectory, ServerJarHashFileName);

        // 1) Fetch the top-level version_manifest_v2 and find the per-version metadata URL.
        var versionMetadataUrl = await ResolveVersionMetadataUrlAsync(minecraftVersion, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(versionMetadataUrl))
            throw new InvalidOperationException(
                $"Could not resolve Mojang manifest entry for Minecraft version '{minecraftVersion}'.");

        // 2) Fetch the per-version metadata and read downloads.server.{url,sha1}.
        var (serverUrl, expectedSha1) = await ResolveServerDownloadAsync(versionMetadataUrl, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(expectedSha1))
            throw new InvalidOperationException(
                $"Mojang manifest for '{minecraftVersion}' is missing a downloads.server entry " +
                $"(snapshots before 1.2.5 don't ship a dedicated server jar from Mojang).");

        // 3) Cache hit: file present + hash sidecar matches manifest -> skip.
        if (File.Exists(jarPath))
        {
            if (await HashMatchesAsync(jarPath, hashPath, expectedSha1, cancellationToken).ConfigureAwait(false))
            {
                progress?.Report(1.0);
                return jarPath;
            }
        }

        // 4) Download + verify.
        await DownloadVerifiedAsync(serverUrl, jarPath, expectedSha1, progress, cancellationToken)
            .ConfigureAwait(false);

        // 5) Record the hash sidecar so subsequent calls hit the fast path.
        await File.WriteAllTextAsync(hashPath, expectedSha1, cancellationToken).ConfigureAwait(false);

        return jarPath;
    }

    private async Task<string?> ResolveVersionMetadataUrlAsync(string minecraftVersion, CancellationToken ct)
    {
        using var response = await _http.GetAsync(_manifestUrl, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("versions", out var versions)) return null;

        foreach (var v in versions.EnumerateArray())
        {
            if (!v.TryGetProperty("id", out var idEl)) continue;
            if (!string.Equals(idEl.GetString(), minecraftVersion, StringComparison.Ordinal)) continue;
            if (!v.TryGetProperty("url", out var urlEl)) return null;
            return urlEl.GetString();
        }
        return null;
    }

    private async Task<(string? Url, string? Sha1)> ResolveServerDownloadAsync(string versionMetadataUrl, CancellationToken ct)
    {
        using var response = await _http.GetAsync(versionMetadataUrl, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("downloads", out var downloads)) return (null, null);
        if (!downloads.TryGetProperty("server", out var server)) return (null, null);

        string? url = server.TryGetProperty("url", out var u) ? u.GetString() : null;
        string? sha1 = server.TryGetProperty("sha1", out var s) ? s.GetString() : null;
        return (url, sha1);
    }

    private async Task DownloadVerifiedAsync(
        string serverUrl, string jarPath, string expectedSha1, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(serverUrl, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength ?? -1L;
        await using (var network = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var output = File.Create(jarPath))
        {
            var buffer = new byte[81920];
            long totalRead = 0;
            int read;
            while ((read = await network.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                totalRead += read;
                if (progress is not null && contentLength > 0)
                {
                    var fraction = (double)totalRead / contentLength;
                    if (fraction > 1.0) fraction = 1.0;
                    progress.Report(fraction);
                }
            }
        }

        var actual = await ComputeSha1Async(jarPath, ct).ConfigureAwait(false);
        if (!string.Equals(actual, expectedSha1, StringComparison.OrdinalIgnoreCase))
        {
            // Drop the corrupt download so a retry actually re-fetches.
            try { File.Delete(jarPath); } catch { /* best-effort */ }
            throw new InvalidOperationException(
                $"server.jar download sha1 mismatch (expected {expectedSha1}, got {actual}).");
        }
        progress?.Report(1.0);
    }

    private static async Task<bool> HashMatchesAsync(string jarPath, string hashPath, string expectedSha1, CancellationToken ct)
    {
        // Sidecar fast path: trust the recorded hash if it matches the manifest. This avoids
        // recomputing sha1 over a ~45 MB jar on every launch. A user who tampers with the file
        // can break the trust contract; the next manifest mismatch will redownload.
        if (File.Exists(hashPath))
        {
            try
            {
                var recorded = (await File.ReadAllTextAsync(hashPath, ct).ConfigureAwait(false)).Trim();
                if (string.Equals(recorded, expectedSha1, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                // Fall through to recompute.
            }
        }

        // Cold path: recompute and write the sidecar.
        var actual = await ComputeSha1Async(jarPath, ct).ConfigureAwait(false);
        var matches = string.Equals(actual, expectedSha1, StringComparison.OrdinalIgnoreCase);
        if (matches)
        {
            try { await File.WriteAllTextAsync(hashPath, expectedSha1, ct).ConfigureAwait(false); }
            catch { /* sidecar is best-effort */ }
        }
        return matches;
    }

    private static async Task<string> ComputeSha1Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var bytes = await SHA1.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
