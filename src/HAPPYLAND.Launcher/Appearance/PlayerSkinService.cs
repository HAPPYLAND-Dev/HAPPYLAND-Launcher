using System.Buffers.Binary;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;

namespace Happyland.Launcher.Appearance;

public sealed class PlayerSkinService(HttpClient http, string cacheDirectory)
{
    private const int MaxSkinBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);

    // Each successful call returns a new bitmap owned by the caller.
    public async Task<Bitmap?> LoadAsync(string? uuid, string? skinServerUrl, CancellationToken ct = default)
    {
        if (!Guid.TryParse(uuid, out var id)) return null;
        var provider = skinServerUrl is null
            ? "https://sessionserver.mojang.com"
            : skinServerUrl.TrimEnd('/') + "/sessionserver";
        if (!IsHttpUri(provider, out var providerUri)) return null;
        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(providerUri.AbsoluteUri + "|" + id.ToString("N"))));
        var cachePath = Path.Combine(cacheDirectory, cacheKey + ".png");
        var cached = await ReadCachedAsync(cachePath, ct, CacheLifetime);
        if (cached is not null) return cached;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var profileUrl = new Uri(providerUri.AbsoluteUri.TrimEnd('/') + "/session/minecraft/profile/" + id.ToString("N"));
            var profile = await DownloadAsync(profileUrl, 256 * 1024, timeout.Token);
            var skinUrl = ReadSkinUrl(profile);
            if (skinUrl is null) return null;
            var data = await DownloadAsync(skinUrl, MaxSkinBytes, timeout.Token);
            var bitmap = DecodeSkin(data);
            if (bitmap is null) return await ReadCachedAsync(cachePath, ct);
            try
            {
                Directory.CreateDirectory(cacheDirectory);
                var temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporary, data, ct);
                    File.Move(temporary, cachePath, overwrite: true);
                }
                finally
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The downloaded skin remains usable when the optional disk cache is unavailable.
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
            return bitmap;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return await ReadCachedAsync(cachePath, ct);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or FormatException or ArgumentException)
        {
            return await ReadCachedAsync(cachePath, ct);
        }
    }

    private async Task<byte[]> DownloadAsync(Uri url, int limit, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("皮肤响应过大。");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await ReadLimitedAsync(stream, limit, ct);
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, int limit, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("皮肤响应过大。");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static Uri? ReadSkinUrl(byte[] profile)
    {
        using var document = JsonDocument.Parse(profile);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var property in properties.EnumerateArray())
        {
            if (property.ValueKind != JsonValueKind.Object
                || !property.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || name.GetString() != "textures"
                || !property.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String)
                continue;
            using var textures = JsonDocument.Parse(Convert.FromBase64String(value.GetString()!));
            var textureRoot = textures.RootElement;
            if (textureRoot.ValueKind != JsonValueKind.Object
                || !textureRoot.TryGetProperty("textures", out var entries) || entries.ValueKind != JsonValueKind.Object
                || !entries.TryGetProperty("SKIN", out var skin) || skin.ValueKind != JsonValueKind.Object
                || !skin.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String)
                return null;
            return IsHttpUri(url.GetString(), out var uri) ? uri : null;
        }
        return null;
    }

    private static bool IsHttpUri(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https")
        {
            uri = parsed;
            return true;
        }
        uri = null!;
        return false;
    }

    private static Bitmap? DecodeSkin(byte[] data)
    {
        if (!PngHeader.IsAcceptableSkinPng(data, MaxSkinBytes) || data.Length < 24 || !data.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            return null;
        var width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20, 4));
        if (width < 64 || width > 2048 || width % 64 != 0 || (height != width && height != width / 2))
            return null;
        using var stream = new MemoryStream(data, writable: false);
        return new Bitmap(stream);
    }

    private static async Task<Bitmap?> ReadCachedAsync(string path, CancellationToken ct, TimeSpan? maxAge = null)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (maxAge is { } age && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) >= age) return null;
            await using var stream = File.OpenRead(path);
            return DecodeSkin(await ReadLimitedAsync(stream, MaxSkinBytes, ct));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
