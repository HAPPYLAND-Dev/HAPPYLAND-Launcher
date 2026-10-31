namespace Happyland.Launcher.Core;

public sealed class MinecraftMirrorHandler : DelegatingHandler
{
    public MinecraftMirrorHandler() : this(new HttpClientHandler()) { }
    public MinecraftMirrorHandler(HttpMessageHandler transport) : base(transport) { }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var official = request.Method == HttpMethod.Get ? GetOfficial(request.RequestUri!) : null;
        if (official is not null)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var mirroredResponse = await base.SendAsync(request, timeout.Token).ConfigureAwait(false);
                if (mirroredResponse.IsSuccessStatusCode) return mirroredResponse;
                mirroredResponse.Dispose();
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            using var fallback = new HttpRequestMessage(HttpMethod.Get, official);
            foreach (var header in request.Headers) fallback.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return await base.SendAsync(fallback, cancellationToken).ConfigureAwait(false);
        }
        var mirror = request.Method == HttpMethod.Get ? GetMirror(request.RequestUri!) : null;
        if (mirror is not null)
        {
            using var mirrored = new HttpRequestMessage(HttpMethod.Get, mirror);
            foreach (var header in request.Headers) mirrored.Headers.TryAddWithoutValidation(header.Key, header.Value);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var response = await base.SendAsync(mirrored, timeout.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return response;
                response.Dispose();
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        }
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public static Uri? GetOfficial(Uri url)
    {
        if (!string.Equals(url.Host, "bmclapi2.bangbang93.com", StringComparison.OrdinalIgnoreCase)) return null;
        var path = url.PathAndQuery;
        if (path.StartsWith("/maven/", StringComparison.Ordinal)) return new Uri("https://libraries.minecraft.net/" + path[7..]);
        if (path.StartsWith("/assets/", StringComparison.Ordinal)) return new Uri("https://resources.download.minecraft.net/" + path[8..]);
        if (path.StartsWith("/v1/objects/", StringComparison.Ordinal)) return new Uri("https://piston-data.mojang.com" + path);
        if (path.StartsWith("/v1/packages/", StringComparison.Ordinal) || path.StartsWith("/mc/game/", StringComparison.Ordinal)) return new Uri("https://piston-meta.mojang.com" + path);
        return null;
    }

    public static Uri? GetMirror(Uri url)
    {
        string? path = url.Host.ToLowerInvariant() switch
        {
            "launchermeta.mojang.com" or "piston-meta.mojang.com" => url.PathAndQuery,
            "launcher.mojang.com" or "piston-data.mojang.com" => url.PathAndQuery,
            "libraries.minecraft.net" => "/maven" + url.PathAndQuery,
            "resources.download.minecraft.net" => "/assets" + url.PathAndQuery,
            _ => null
        };
        return path is null ? null : new Uri("https://bmclapi2.bangbang93.com" + path);
    }
}
