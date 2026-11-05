using System.Security.Cryptography;
using Happyland.Contracts;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<Publication>(builder.Configuration.GetSection("Publication"));
var app = builder.Build();

app.MapGet("/api/launcher", async (HttpContext context, IOptionsMonitor<Publication> options, CancellationToken cancellationToken) =>
{
    var publication = options.CurrentValue;
    if (string.IsNullOrWhiteSpace(publication.Version) || string.IsNullOrWhiteSpace(publication.MrpackPath) || publication.Servers.Count == 0)
        return Results.Problem("整合包版本、文件路径和服务器列表必须在后端配置中设置。", statusCode: 503);
    if (publication.Shortcuts is null || publication.Shortcuts.Any(shortcut => shortcut is null ||
            string.IsNullOrWhiteSpace(shortcut.Id) || string.IsNullOrWhiteSpace(shortcut.Name) || !IsHttpUrl(shortcut.Url) ||
            (shortcut.IconPixels is { } pixels ? !LauncherShortcut.IsValidIconPixels(pixels)
                : shortcut.Icon is not ("skin" or "wiki" or "link") && !IsHttpUrl(shortcut.Icon))) ||
        publication.Shortcuts.Select(shortcut => shortcut.Id).Distinct(StringComparer.Ordinal).Count() != publication.Shortcuts.Count)
        return Results.Problem("后端快捷链接配置无效，请检查标识、名称、链接和图标。", statusCode: 503);
    if (publication.Servers.Any(server => string.IsNullOrWhiteSpace(server.Id) || string.IsNullOrWhiteSpace(server.Name) || string.IsNullOrWhiteSpace(server.Host) || server.Port is < 1 or > 65535 || !HasValidRoutes(server.Routes)) ||
        publication.Servers.Select(server => server.Id).Distinct(StringComparer.Ordinal).Count() != publication.Servers.Count ||
        publication.SkinServers.Any(site => string.IsNullOrWhiteSpace(site.Id) || string.IsNullOrWhiteSpace(site.Name) || !Uri.TryCreate(site.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
        (!string.IsNullOrWhiteSpace(publication.PublicBaseUrl) && !Uri.TryCreate(publication.PublicBaseUrl, UriKind.Absolute, out _)))
        return Results.Problem("后端服务器、线路或皮肤站配置无效。", statusCode: 503);
    var path = Path.GetFullPath(publication.MrpackPath, app.Environment.ContentRootPath);
    if (!File.Exists(path))
        return Results.Problem("整合包文件不存在。", statusCode: 503);
    await using var stream = File.OpenRead(path);
    var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    var baseUrl = string.IsNullOrWhiteSpace(publication.PublicBaseUrl) ? $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}/" : publication.PublicBaseUrl.TrimEnd('/') + "/";
    var packUrl = new Uri(new Uri(baseUrl), "api/pack").ToString();
    return Results.Ok(new LauncherConfig(publication.Version, packUrl, stream.Length, hash,
        publication.MicrosoftClientId, publication.Servers, publication.SkinServers,
        publication.HomeNotice, publication.Shortcuts));
});
app.MapGet("/api/launcher/updates", (long after, IOptionsMonitor<Publication> options) =>
{
    if (after < 0) return Results.BadRequest("当前版本号不能为负数。");
    var updates = options.CurrentValue.LauncherUpdate;
    if (updates is null || updates.LatestVersion < 0 || updates.Releases is null ||
        updates.Releases.Any(release => release is null || release.Version <= 0 ||
            release.Version > updates.LatestVersion || release.Version > DateTimeOffset.MaxValue.ToUnixTimeSeconds() ||
            string.IsNullOrWhiteSpace(release.Title) || string.IsNullOrWhiteSpace(release.Content)) ||
        updates.Releases.Select(release => release.Version).Distinct().Count() != updates.Releases.Count ||
        (updates.LatestVersion > 0 && !updates.Releases.Any(release => release.Version == updates.LatestVersion)))
        return Results.Problem("后端启动器更新配置无效，请检查最新时间戳、版本记录及公告正文。", statusCode: 503);
    return Results.Ok(new LauncherUpdateFeed(updates.LatestVersion,
        updates.Releases.Where(release => release.Version > after && release.Version <= updates.LatestVersion)
            .OrderByDescending(release => release.Version).ToArray()));
});
app.MapGet("/api/announcements", (IOptionsMonitor<Publication> options) =>
{
    var announcements = options.CurrentValue.Announcements;
    if (announcements is null || announcements.Any(announcement => announcement is null ||
            string.IsNullOrWhiteSpace(announcement.Id) || string.IsNullOrWhiteSpace(announcement.Title) ||
            announcement.PublishedAt == default || string.IsNullOrWhiteSpace(announcement.Content)) ||
        announcements.Select(announcement => announcement.Id).Distinct(StringComparer.Ordinal).Count() != announcements.Count)
        return Results.Problem("后端更新公告配置无效，请检查标识、标题、发布时间和正文。", statusCode: 503);
    return Results.Ok(announcements.OrderByDescending(announcement => announcement.PublishedAt)
        .ThenBy(announcement => announcement.Id, StringComparer.Ordinal).ToArray());
});
app.MapGet("/api/pack", (IOptionsMonitor<Publication> options) =>
{
    var path = options.CurrentValue.MrpackPath;
    if (string.IsNullOrWhiteSpace(path)) return Results.NotFound();
    var fullPath = Path.GetFullPath(path, app.Environment.ContentRootPath);
    return File.Exists(fullPath) ? Results.File(fullPath, "application/zip", enableRangeProcessing: true) : Results.NotFound();
});
app.Run();

static bool IsHttpUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
    uri.Scheme is "http" or "https";

static bool HasValidRoutes(IReadOnlyList<ServerRoute>? routes) => routes is null ||
    routes.All(route => route is not null && route.Id != "main" &&
        !string.IsNullOrWhiteSpace(route.Id) && !string.IsNullOrWhiteSpace(route.Name) &&
        !string.IsNullOrWhiteSpace(route.Host) && route.Port is >= 1 and <= 65535) &&
    routes.Select(route => route.Id).Distinct(StringComparer.Ordinal).Count() == routes.Count;

public sealed class Publication
{
    public string PublicBaseUrl { get; set; } = "";
    public string Version { get; set; } = "";
    public string MrpackPath { get; set; } = "";
    public string MicrosoftClientId { get; set; } = "";
    public List<GameServer> Servers { get; set; } = [];
    public List<SkinServer> SkinServers { get; set; } = [];
    public string HomeNotice { get; set; } = "";
    public List<LauncherShortcut> Shortcuts { get; set; } = [];
    public List<LauncherAnnouncement> Announcements { get; set; } = [];
    public LauncherUpdatePublication LauncherUpdate { get; set; } = new();
}

public sealed class LauncherUpdatePublication
{
    public long LatestVersion { get; set; }
    public List<LauncherRelease> Releases { get; set; } = [];
}
