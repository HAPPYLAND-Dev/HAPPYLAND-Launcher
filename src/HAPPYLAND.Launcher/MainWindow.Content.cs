using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Happyland.Contracts;
using Happyland.Launcher.Appearance;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;

namespace Happyland.Launcher;

public sealed partial class MainWindow
{
    private readonly StackPanel shortcuts = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly TextBlock homeNotice = new() { TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
    private readonly ScrollViewer homeNoticeArea = new() { MaxHeight = 84, IsVisible = false,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    private readonly ComboBox routes = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Dictionary<string, string> selectedRoutes = new(StringComparer.Ordinal);
    private readonly List<Bitmap> shortcutBitmaps = [];
    private IReadOnlyList<LauncherShortcut> displayedShortcuts = [];
    private RouteOption[] routeOptions = [];
    private string? routesServerId;
    private CancellationTokenSource? shortcutCancellation;
    private bool updatingRoutes;
    private readonly Button uninstall = new() { Content = "卸载游戏" };
    private readonly Button reinstallJava = new() { Content = "重装运行库" };

    private Control BuildShortcutBar()
    {
        RebuildShortcuts([]);
        return new ScrollViewer
        {
            Content = shortcuts, Height = 40, Margin = new Thickness(18, 0, 18, 12),
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
    }

    private Button ShortcutButton(string name, Control icon, Func<Task> action)
    {
        var button = new Button { Width = 32, Height = 32, MinHeight = 0, Padding = new Thickness(0), Content = icon };
        ToolTip.SetTip(button, name);
        Avalonia.Automation.AutomationProperties.SetName(button, name);
        button.Click += async (_, _) => await ExecuteAsync(action, "暂时无法打开，请稍后重试。");
        return button;
    }

    private void RefreshHomeContent()
    {
        homeNotice.Text = config!.HomeNotice;
        homeNoticeArea.IsVisible = !string.IsNullOrWhiteSpace(config.HomeNotice);
        var links = config.Shortcuts ?? [];
        if (!displayedShortcuts.SequenceEqual(links)) RebuildShortcuts(links);
    }

    private void RebuildShortcuts(IReadOnlyList<LauncherShortcut> links)
    {
        shortcutCancellation?.Cancel();
        shortcuts.Children.Clear();
        foreach (var bitmap in shortcutBitmaps) bitmap.Dispose();
        shortcutBitmaps.Clear();
        displayedShortcuts = links.ToArray();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        shortcutCancellation = cancellation;
        var downloads = new List<Task>();
        foreach (var link in links)
        {
            var kind = link.Icon switch { "skin" => PixelIconKind.Skin, "wiki" => PixelIconKind.Book, _ => PixelIconKind.Link };
            var content = new Grid { Width = 16, Height = 16, Children = { new PixelIcon { Kind = kind } } };
            var button = ShortcutButton(link.Name, content, () =>
            {
                if (!Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                    throw new UserActionException("链接暂时无法打开。");
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                return Task.CompletedTask;
            });
            shortcuts.Children.Add(button);
            if (Uri.TryCreate(link.Icon, UriKind.Absolute, out var iconUri) && iconUri.Scheme is "http" or "https")
                downloads.Add(LoadShortcutIconAsync(content, iconUri, cancellation.Token));
        }
        shortcuts.Children.Add(ShortcutButton("游戏文件夹", new PixelIcon { Kind = PixelIconKind.Folder }, () =>
        { OpenGameFolder(); return Task.CompletedTask; }));
        shortcuts.Children.Add(ShortcutButton("更新公告", new PixelIcon { Kind = PixelIconKind.Bell }, ShowAnnouncementsAsync));
        foreach (var button in shortcuts.Children.OfType<Button>()) button.IsEnabled = !busy;
        _ = FinishShortcutIconsAsync(downloads, cancellation);
    }

    private async Task LoadShortcutIconAsync(Grid target, Uri uri, CancellationToken ct)
    {
        try
        {
            const int limit = 2 * 1024 * 1024;
            using var response = await skinHttp.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Shortcut icon exceeds download limit.");
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            using var data = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(buffer, ct)) != 0)
            {
                if (data.Length + count > limit) throw new InvalidDataException("Shortcut icon exceeds download limit.");
                data.Write(buffer, 0, count);
            }
            var bytes = data.ToArray();
            var bitmap = await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes, writable: false);
                return Bitmap.DecodeToWidth(stream, 32, BitmapInterpolationMode.None);
            }, ct);
            if (ct.IsCancellationRequested) { bitmap.Dispose(); return; }
            shortcutBitmaps.Add(bitmap);
            target.Children.Clear();
            target.Children.Add(new Image { Source = bitmap, Width = 16, Height = 16, Stretch = Stretch.Uniform });
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is HttpRequestException or IOException or ArgumentException)
        { logger.Error("Could not load shortcut icon", e); }
    }

    private async Task FinishShortcutIconsAsync(IEnumerable<Task> downloads, CancellationTokenSource cancellation)
    {
        try { await Task.WhenAll(downloads); }
        finally
        {
            if (ReferenceEquals(shortcutCancellation, cancellation)) shortcutCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task ShowAnnouncementsAsync()
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        request.CancelAfter(TimeSpan.FromSeconds(8));
        var items = await http.GetFromJsonAsync<LauncherAnnouncement[]>(new Uri(new Uri(LauncherDeployment.BackendUrl), "api/announcements"), request.Token)
            ?? throw new UserActionException("公告暂时无法读取。");
        var view = new AnnouncementView(items);
        await ShowOverlayAsync(view, "更新公告", view.Cancel, view.Result, view.FocusFirstField, scrollContent: false);
    }

    private static bool SameServers(IReadOnlyList<GameServer> left, IReadOnlyList<GameServer> right) => left.Count == right.Count &&
        left.Zip(right).All(pair => pair.First.Id == pair.Second.Id && pair.First.Name == pair.Second.Name &&
            pair.First.Host == pair.Second.Host && pair.First.Port == pair.Second.Port &&
            (pair.First.Routes ?? []).SequenceEqual(pair.Second.Routes ?? []));

    private void ConfigureRoutes()
    {
        if (servers.SelectedItem is not GameServer server) return;
        var endpoints = new[] { new ServerRoute("main", "主线路", server.Host, server.Port) }.Concat(server.Routes ?? []).ToArray();
        if (routesServerId == server.Id && routeOptions.Select(option => option.Route).SequenceEqual(endpoints)) return;
        var old = routeOptions;
        updatingRoutes = true;
        try
        {
            routesServerId = server.Id;
            routeOptions = endpoints.Select(endpoint => old.FirstOrDefault(option => option.Route == endpoint) ?? new RouteOption(endpoint)).ToArray();
            routes.ItemsSource = routeOptions;
            selectedRoutes.TryGetValue(server.Id, out var selected);
            routes.SelectedItem = routeOptions.FirstOrDefault(option => option.Route.Id == selected) ?? routeOptions[0];
        }
        finally { updatingRoutes = false; }
        UpdateSelectedRouteNumbers();
    }

    private void WireRoutes()
    {
        routes.ItemTemplate = new FuncDataTemplate<RouteOption>((option, _) =>
        {
            if (option is null) return null;
            var name = new TextBlock { Text = option.Route.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var latency = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Classes = { "small", "muted" } };
            latency.Bind(TextBlock.TextProperty, new Binding(nameof(RouteOption.Latency)));
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,Auto"), Children = { name, latency } };
            Grid.SetColumn(latency, 2);
            return row;
        });
        Avalonia.Automation.AutomationProperties.SetName(routes, "连接线路");
        routes.SelectionChanged += async (_, _) =>
        {
            if (updatingRoutes) return;
            if (servers.SelectedItem is GameServer server && routes.SelectedItem is RouteOption option)
                selectedRoutes[server.Id] = option.Route.Id;
            UpdateSelectedRouteNumbers();
            if (!initializing && !busy) await TrySavePreferencesAsync();
        };
    }

    private void UpdateSelectedRouteNumbers() => UpdateServerNumbers((routes.SelectedItem as RouteOption)?.Status);

    private async Task RefreshRouteAsync(RouteOption option, CancellationToken ct)
    {
        ServerStatus? status;
        try { status = await pinger.PingAsync(option.Route.Host, option.Route.Port, TimeSpan.FromSeconds(4), ct); }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or System.Text.Json.JsonException or InvalidOperationException or FormatException)
        { if (!ct.IsCancellationRequested) logger.Error("Could not read route status", e); status = null; }
        if (ct.IsCancellationRequested) return;
        option.SetStatus(status);
        if (ReferenceEquals(routes.SelectedItem, option)) UpdateServerNumbers(status);
    }

    private sealed class RouteOption(ServerRoute route) : INotifyPropertyChanged
    {
        public ServerRoute Route { get; } = route;
        public ServerStatus? Status { get; private set; }
        public string Latency => Status is null ? "—" : Status.LatencyMs + " ms";
        public event PropertyChangedEventHandler? PropertyChanged;
        public void SetStatus(ServerStatus? value)
        {
            var previous = Latency;
            Status = value;
            if (Latency != previous) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Latency)));
        }
    }
}
