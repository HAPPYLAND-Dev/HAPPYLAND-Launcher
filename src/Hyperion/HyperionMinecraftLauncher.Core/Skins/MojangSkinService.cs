using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;

/// <summary>
/// Production <see cref="ISkinService"/> implementation hitting
/// <c>https://api.minecraftservices.com/minecraft/profile/*</c>. Each call attaches the
/// bearer access token returned by Microsoft sign-in; HTTP failures are translated into
/// <see cref="SkinUploadFailedException"/> with the upstream status code in the message.
/// </summary>
public sealed class MojangSkinService : ISkinService
{
    /// <summary>Public base URL of the Minecraft profile API. Lifted from the official launcher's network traffic.</summary>
    public const string BaseUrl = "https://api.minecraftservices.com";

    private const string OfflineMessage = "Skin operations require a Microsoft account.";

    private readonly HttpClient _http;

    public MojangSkinService() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }) { }

    public MojangSkinService(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public async Task UploadSkinAsync(string accessToken, byte[] pngBytes, SkinVariant variant, CancellationToken cancellationToken)
    {
        EnsureToken(accessToken);
        if (pngBytes is null || pngBytes.Length == 0)
            throw new SkinUploadFailedException("Skin upload failed: empty PNG bytes.");

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(VariantToWire(variant)), "variant");
        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "skin.png");

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/minecraft/profile/skins")
        {
            Content = content,
        };
        AddAuth(req, accessToken);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new SkinUploadFailedException($"Skin upload failed: {ex.Message}", ex);
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
                throw await ToFailureAsync(resp, "Skin upload", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<PlayerProfile> GetProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        EnsureToken(accessToken);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/minecraft/profile");
        AddAuth(req, accessToken);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new SkinUploadFailedException($"Get profile failed: {ex.Message}", ex);
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
                throw await ToFailureAsync(resp, "Get profile", cancellationToken).ConfigureAwait(false);

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return ParseProfile(stream);
            }
            catch (Exception ex)
            {
                throw new SkinUploadFailedException($"Get profile failed: malformed JSON response - {ex.Message}", ex);
            }
        }
    }

    /// <inheritdoc />
    public async Task SetActiveCapeAsync(string accessToken, string capeId, CancellationToken cancellationToken)
    {
        EnsureToken(accessToken);
        if (string.IsNullOrWhiteSpace(capeId))
            throw new SkinUploadFailedException("Set cape failed: empty cape id.");

        var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["capeId"] = capeId });
        using var req = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/minecraft/profile/capes/active")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        AddAuth(req, accessToken);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new SkinUploadFailedException($"Set cape failed: {ex.Message}", ex);
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
                throw await ToFailureAsync(resp, "Set cape", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task ClearActiveCapeAsync(string accessToken, CancellationToken cancellationToken)
    {
        EnsureToken(accessToken);
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/minecraft/profile/capes/active");
        AddAuth(req, accessToken);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new SkinUploadFailedException($"Clear cape failed: {ex.Message}", ex);
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
                throw await ToFailureAsync(resp, "Clear cape", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Parses a Mojang profile JSON payload into a <see cref="PlayerProfile"/>. Public so
    /// fixture-driven tests can exercise the parser without a network stub.
    /// </summary>
    public static PlayerProfile ParseProfile(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var doc = JsonDocument.Parse(stream);
        return ParseProfile(doc.RootElement);
    }

    /// <summary>Overload that consumes a string body (useful for tests).</summary>
    public static PlayerProfile ParseProfile(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseProfile(doc.RootElement);
    }

    private static PlayerProfile ParseProfile(JsonElement root)
    {
        string id = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString() ?? string.Empty : string.Empty;
        string name = root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
            ? nameEl.GetString() ?? string.Empty : string.Empty;

        var skins = new List<OwnedSkin>();
        if (root.TryGetProperty("skins", out var skinsEl) && skinsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in skinsEl.EnumerateArray())
            {
                skins.Add(new OwnedSkin
                {
                    Id = StringOrEmpty(s, "id"),
                    State = StringOrEmpty(s, "state"),
                    Url = StringOrEmpty(s, "url"),
                    Variant = StringOrEmpty(s, "variant"),
                    Alias = NullableString(s, "alias"),
                });
            }
        }

        var capes = new List<OwnedCape>();
        if (root.TryGetProperty("capes", out var capesEl) && capesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in capesEl.EnumerateArray())
            {
                capes.Add(new OwnedCape
                {
                    Id = StringOrEmpty(c, "id"),
                    State = StringOrEmpty(c, "state"),
                    Url = StringOrEmpty(c, "url"),
                    Alias = StringOrEmpty(c, "alias"),
                });
            }
        }

        return new PlayerProfile { Id = id, Name = name, Skins = skins, Capes = capes };
    }

    private static string StringOrEmpty(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? string.Empty : string.Empty;

    private static string? NullableString(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() : null;

    private static void EnsureToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new SkinUploadFailedException(OfflineMessage);
    }

    private static void AddAuth(HttpRequestMessage req, string accessToken)
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private static string VariantToWire(SkinVariant v) => v switch
    {
        SkinVariant.Slim => "slim",
        _ => "classic",
    };

    private static async Task<SkinUploadFailedException> ToFailureAsync(
        HttpResponseMessage resp, string operation, CancellationToken cancellationToken)
    {
        string body;
        try { body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false); }
        catch { body = string.Empty; }
        var snippet = body.Length > 200 ? body[..200] + "..." : body;
        var msg = string.IsNullOrEmpty(snippet)
            ? $"{operation} failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}."
            : $"{operation} failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} - {snippet}";
        return new SkinUploadFailedException(msg);
    }
}
