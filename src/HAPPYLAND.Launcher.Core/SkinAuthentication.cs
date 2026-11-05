using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Downloads;

namespace Happyland.Launcher.Core;

public sealed record SkinSession(AuthResult Account, string ClientToken);

public static class SkinAuthentication
{
    public static async Task<SkinSession> SignInAsync(HttpClient http, string baseUrl, string username, string password, CancellationToken ct = default)
    {
        var endpoint = baseUrl.TrimEnd('/') + "/authserver/authenticate";
        using var response = await http.PostAsJsonAsync(endpoint, new { agent = new { name = "Minecraft", version = 1 }, username, password, clientToken = Guid.NewGuid().ToString("N"), requestUser = true }, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            throw new UserActionException("皮肤站登录失败，请检查账号和密码。");
        response.EnsureSuccessStatusCode();
        return ReadSession(await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct));
    }

    public static async Task<SkinSession?> ResumeAsync(HttpClient http, string baseUrl, SkinSession saved, CancellationToken ct = default)
    {
        var endpoint = baseUrl.TrimEnd('/') + "/authserver/";
        var tokens = new { accessToken = saved.Account.AccessToken, clientToken = saved.ClientToken };
        using var validation = await http.PostAsJsonAsync(endpoint + "validate", tokens, ct);
        if (validation.IsSuccessStatusCode) return saved;
        if (validation.StatusCode != System.Net.HttpStatusCode.Forbidden) validation.EnsureSuccessStatusCode();
        using var response = await http.PostAsJsonAsync(endpoint + "refresh", new { tokens.accessToken, tokens.clientToken, requestUser = true }, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden) return null;
        response.EnsureSuccessStatusCode();
        return ReadSession(await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct));
    }

    private static SkinSession ReadSession(JsonElement result)
    {
        if (!result.TryGetProperty("selectedProfile", out var profile) || profile.ValueKind != JsonValueKind.Object)
            throw new UserActionException("请先在皮肤站添加或选择角色。");
        var account = new AuthResult { Username = profile.GetProperty("name").GetString()!, Uuid = profile.GetProperty("id").GetString()!, AccessToken = result.GetProperty("accessToken").GetString()!, IsOffline = false };
        return new SkinSession(account, result.GetProperty("clientToken").GetString()!);
    }

    public static async Task<string> EnsureInjectorAsync(HttpClient http, string root, CancellationToken ct = default)
    {
        var info = await http.GetFromJsonAsync<JsonElement>("https://authlib-injector.yushi.moe/artifact/latest.json", ct);
        var hash = info.GetProperty("checksums").GetProperty("sha256").GetString()!;
        var path = Path.Combine(root, "authlib-injector-" + hash + ".jar");
        Directory.CreateDirectory(root);
        if (File.Exists(path))
        {
            await using var cached = File.OpenRead(path);
            var cachedHash = Convert.ToHexString(await SHA256.HashDataAsync(cached, ct));
            if (string.Equals(cachedHash, hash, StringComparison.OrdinalIgnoreCase)) return path;
        }
        await new AdaptiveDownloader(http).DownloadAsync(
            new DownloadFile(info.GetProperty("download_url").GetString()!, path, Hash: hash, HashAlgorithm: HashAlgorithmName.SHA256),
            cancellationToken: ct);
        return path;
    }
}
