using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Happyland.Contracts;

namespace Happyland.Launcher;

public sealed partial class MainWindow
{
    private long dismissedLauncherVersion;

    private async Task CheckLauncherUpdatesAsync()
    {
        LauncherUpdateFeed feed;
        using (var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
        {
            request.CancelAfter(TimeSpan.FromSeconds(8));
            feed = await ReadBackendAsync<LauncherUpdateFeed>(
                "api/launcher/updates?after=" + LauncherVersion.Code.ToString(CultureInfo.InvariantCulture), request.Token);
        }
        if (feed.LatestVersion <= LauncherVersion.Code) return;
        var releases = feed.Releases.Where(release => release.Version > LauncherVersion.Code && release.Version <= feed.LatestVersion)
            .OrderByDescending(release => release.Version).ToArray();
        if (releases.Length == 0 || releases[0].Version != feed.LatestVersion)
            throw new InvalidDataException("后端缺少最新启动器版本的更新公告。");
        var mandatory = releases.Any(release => release.Mandatory);
        if (!mandatory && dismissedLauncherVersion == feed.LatestVersion) return;

        var view = new AnnouncementView(releases.Select(release => new LauncherAnnouncement(
            release.Version.ToString(CultureInfo.InvariantCulture), release.Title,
            DateTimeOffset.FromUnixTimeSeconds(release.Version), release.Content)).ToArray());
        var notice = new TextBlock
        {
            Text = mandatory ? "此更新为必须更新，请按公告说明更换启动器后重新打开。" : "发现新版启动器，更新内容如下。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 12, 16, 0)
        };
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { notice, view } };
        Grid.SetRow(view, 1);
        await ShowOverlayAsync(content, mandatory ? "必须更新启动器" : "启动器更新", view.Cancel,
            view.Result, view.FocusFirstField, scrollContent: false, allowDismiss: !mandatory);
        lifetime.Token.ThrowIfCancellationRequested();
        dismissedLauncherVersion = feed.LatestVersion;
    }
}
