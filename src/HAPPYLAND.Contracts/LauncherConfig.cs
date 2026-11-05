namespace Happyland.Contracts;

public sealed record LauncherConfig(string PackVersion, string PackUrl, long PackSize, string PackSha256,
    string MicrosoftClientId, IReadOnlyList<GameServer> Servers, IReadOnlyList<SkinServer> SkinServers,
    string HomeNotice = "", IReadOnlyList<LauncherShortcut>? Shortcuts = null);
public sealed record GameServer(string Id, string Name, string Host, int Port = 25565,
    IReadOnlyList<ServerRoute>? Routes = null)
{
    // Configuration binding represents an empty Routes array as an empty scalar.
    public GameServer() : this("", "", "") { }

    public IEnumerable<ServerRoute> GetRoutes() =>
        new[] { new ServerRoute("main", "主线路", Host, Port) }.Concat(Routes ?? []);
}
public sealed record ServerRoute(string Id, string Name, string Host, int Port = 25565);
public sealed record SkinServer(string Id, string Name, string Url);
public sealed record LauncherShortcut(string Id, string Name, string Url, string Icon = "link",
    IReadOnlyList<string>? IconPixels = null)
{
    public static bool IsValidIconPixels(IReadOnlyList<string> rows) => rows.Count == 16 &&
        rows.All(row => row is { Length: 16 } && row.All(pixel => pixel is '0' or '1'));
}
public sealed record LauncherAnnouncement(string Id, string Title, DateTimeOffset PublishedAt, string Content);

public sealed record LauncherRelease(long Version, bool Mandatory, string Title, string Content);
public sealed record LauncherUpdateFeed(long LatestVersion, IReadOnlyList<LauncherRelease> Releases);
