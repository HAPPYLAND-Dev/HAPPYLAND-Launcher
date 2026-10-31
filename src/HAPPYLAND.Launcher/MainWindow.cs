using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using Avalonia;
using Avalonia.Animation;
using System.Diagnostics;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Happyland.Contracts;
using Happyland.Launcher.Appearance;
using Happyland.Launcher.Core;
using TechTeaStudio.HyperionMinecraftLauncher.App.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth.Accounts;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;

namespace Happyland.Launcher;

public sealed partial class MainWindow : Window
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly HttpClient skinHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly CancellationTokenSource lifetime = new();
    private readonly PackManager packs;
    private readonly GameManager game;
    private readonly PlayerSkinService skins;
    private readonly SkinCredentialStore skinCredentials;
    private readonly string root;
    private readonly ILauncherLogger logger;
    private readonly List<LauncherAccount> accountList = [];
    private readonly ComboBox accounts = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "添加账号", MinHeight = 32 };
    private readonly ComboBox servers = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "暂无服务器" };
    private readonly ToastHost toast = new();
    private readonly TextBlock actionText = Label("下载游戏", "title");
    private readonly Button action = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Height = 30 };
    private readonly Button addAccount = new() { Width = 28, MinHeight = 0, VerticalAlignment = VerticalAlignment.Stretch, Padding = new Thickness(0), Content = new PixelIcon { Kind = PixelIconKind.Plus, Width = 12, Height = 12 } };
    private readonly StackPanel mods = new() { Spacing = 0 };
    private readonly TextBlock modCount = Label("尚未安装整合包", "muted");
    private readonly SemaphoreSlim preferenceSaveLock = new(1, 1);
    private readonly ComboBox theme = new() { ItemsSource = PixelTheme.PresetNames, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox autoPalette = new() { Content = "跟随背景配色", IsChecked = true };
    private readonly TextBlock backgroundLabel = Label("无", "muted");
    private readonly Image backgroundImage = new() { Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
    private readonly TransitioningContentControl page = new();
    private readonly Grid viewport = new();
    private readonly PixelWindowChrome chrome = new();
    private readonly Dictionary<string, Button> navigation = [];
    private readonly List<Control> editingControls = [];
    private readonly MenuItem addMicrosoft = new() { Header = "添加微软账号" };
    private readonly MenuItem addSkin = new() { Header = "添加皮肤站账号" };
    private Control homePage = null!, modsPage = null!, settingsPage = null!, gameSettingsPage = null!;
    private Settings preferences = new();
    private MicrosoftAuthService? microsoft;
    private string? microsoftClientId;
    private LauncherConfig? config;
    private InstalledPack? installed;
    private Bitmap? backgroundBitmap;
    private string? selectedServerId;
    private bool busy, initializing = true;
    private bool updatingServers;
    private CancellationTokenSource? avatarCancellation;

    public MainWindow()
    {
        Title = "HAPPYLAND Launcher";
        SystemDecorations = SystemDecorations.None;
        CanResize = true;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        RenderOptions.SetTextRenderingMode(this, TextRenderingMode.Alias);
        using (var icon = AssetLoader.Open(new Uri("avares://HAPPYLAND.Launcher/Assets/server-icon.png"))) Icon = new WindowIcon(icon);
        root = Environment.GetEnvironmentVariable("HAPPYLAND_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HAPPYLAND");
        logger = new SerilogLauncherLogger(Path.Combine(root, "logs"));
        packs = new PackManager(root, http);
        game = new GameManager(root);
        skins = new PlayerSkinService(skinHttp, Path.Combine(root, "skin-cache"));
        skinCredentials = new SkinCredentialStore(root);
        LoadPreferences();
        LoadSkinAccounts();
        foreach (var pair in preferences.SelectedRoutes ?? []) selectedRoutes[pair.Key] = pair.Value;
        selectedServerId = preferences.SelectedServerId;
        theme.SelectedItem = PixelTheme.PresetNames.Contains(preferences.Theme) ? preferences.Theme : "雾蓝";
        autoPalette.IsChecked = preferences.AutoPalette;
        LoadBackground();
        InitializeOptions();
        ApplyAppearance();
        BuildInterface();
        WireActions();
        UpdateAccounts(preferences.SelectedAccount);
        SetAvailability();
        initializing = false;
        InitializeWindowSize();
        Opened += async (_, _) =>
        {
            await ExecuteAsync(RefreshAsync, "暂时无法连接，请稍后重试。");
            serverTimer.Start();
        };
        Closed += (_, _) =>
        {
            lifetime.Cancel();
            cancelOverlay?.Invoke();
            serverTimer.Stop(); pingCancellation?.Cancel();
            shortcutCancellation?.Cancel();
            foreach (var bitmap in shortcutBitmaps) bitmap.Dispose();
            toast.Hide();
            runningGame?.Dispose();
            avatarCancellation?.Cancel();
            backgroundBitmap?.Dispose();
            foreach (var account in accountList) account.Skin?.Dispose();
            http.Dispose(); skinHttp.Dispose();
        };
    }

    private Control BuildWindowFrame()
    {
        chrome.Child = viewport;
        chrome.Bind(PixelWindowChrome.BackgroundProperty, this.GetResourceObservable("PixelBackground"));
        chrome.Bind(PixelWindowChrome.BorderBrushProperty, this.GetResourceObservable("PixelBorder"));
        WindowEdge? EdgeAt(Point point)
        {
            if (WindowState != WindowState.Normal) return null;
            var left = point.X < 5;
            var right = point.X >= chrome.Bounds.Width - 5;
            var top = point.Y < 5;
            var bottom = point.Y >= chrome.Bounds.Height - 5;
            if (top && left) return WindowEdge.NorthWest;
            if (top && right) return WindowEdge.NorthEast;
            if (bottom && left) return WindowEdge.SouthWest;
            if (bottom && right) return WindowEdge.SouthEast;
            if (left) return WindowEdge.West;
            if (right) return WindowEdge.East;
            if (top) return WindowEdge.North;
            if (bottom) return WindowEdge.South;
            return null;
        }
        WindowEdge? previousEdge = null;
        chrome.PointerMoved += (_, e) =>
        {
            var edge = EdgeAt(e.GetPosition(chrome));
            if (edge == previousEdge) return;
            previousEdge = edge;
            chrome.Cursor = new Cursor(edge switch
            {
            WindowEdge.NorthWest => StandardCursorType.TopLeftCorner,
            WindowEdge.NorthEast => StandardCursorType.TopRightCorner,
            WindowEdge.SouthWest => StandardCursorType.BottomLeftCorner,
            WindowEdge.SouthEast => StandardCursorType.BottomRightCorner,
            WindowEdge.West or WindowEdge.East => StandardCursorType.SizeWestEast,
            WindowEdge.North or WindowEdge.South => StandardCursorType.SizeNorthSouth,
                _ => StandardCursorType.Arrow
            });
        };
        chrome.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(chrome).Properties.IsLeftButtonPressed && EdgeAt(e.GetPosition(chrome)) is { } edge)
            {
                BeginResizeDrag(edge, e);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        return chrome;
    }

    private void WireActions()
    {
        WireRoutes();
        action.Click += async (_, _) => await ExecuteAsync(PrimaryAsync);
        uninstall.Click += async (_, _) => await ExecuteAsync(UninstallGameAsync, "游戏未能卸载，请重试。");
        reinstallJava.Click += async (_, _) => await ExecuteAsync(ReinstallJavaAsync, "运行库未能重装，请重试。");
        var accountMenu = new MenuFlyout { ItemsSource = new[] { addMicrosoft, addSkin } };
        addAccount.Flyout = accountMenu;
        addMicrosoft.Click += async (_, _) => await ExecuteAsync(AddMicrosoftAsync, "登录失败，请重试。");
        addSkin.Click += async (_, _) => await ExecuteAsync(AddSkinAsync, "登录失败，请重试。");
        servers.SelectionChanged += async (_, _) =>
        {
            if (updatingServers) return;
            if (servers.SelectedItem is GameServer server) selectedServerId = server.Id;
            ConfigureRoutes();
            _ = RefreshServerStatusAsync();
            if (!initializing && !busy) await TrySavePreferencesAsync();
        };
        servers.ItemTemplate = new FuncDataTemplate<GameServer>((server, _) => Label(server?.Name ?? ""));
        accounts.SelectionChanged += async (_, _) =>
        {
            if (!initializing && !busy) await TrySavePreferencesAsync();
            await LoadSelectedAvatarAsync();
        };
        theme.SelectionChanged += async (_, _) =>
        {
            if (initializing) return;
            ApplyAppearance(); await TrySavePreferencesAsync();
        };
        autoPalette.IsCheckedChanged += async (_, _) =>
        {
            if (initializing) return;
            ApplyAppearance(); await TrySavePreferencesAsync();
        };
    }

    private void ShowPage(string title)
    {
        page.Content = title switch { "Mod 管理" => modsPage, "设置" => settingsPage, "游戏选项" => gameSettingsPage, _ => homePage };
        foreach (var pair in navigation) pair.Value.Classes.Set("selected", pair.Key == title);
    }

    private Control SolidSurface(Control child)
    {
        var surface = new Border { Child = child };
        surface.Bind(Border.BackgroundProperty, this.GetResourceObservable("PixelBackground"));
        return surface;
    }

    private async Task ExecuteAsync(Func<Task> operation, string errorText = "操作失败，请重试。")
    {
        if (busy) return;
        busy = true; SetAvailability();
        try { await operation(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e)
        {
            logger.Error("Launcher operation failed", e);
            SetStatus(e is UserActionException or CredentialStorageException ? e.Message : errorText, true);
        }
        finally { busy = false; SetAvailability(); RestoreOverlayFocus(); }
    }

    private void SetAvailability()
    {
        action.IsEnabled = !busy && !gameRunning; accounts.IsEnabled = !busy; servers.IsEnabled = !busy && config is not null;
        addAccount.IsEnabled = !busy && config is not null;
        addMicrosoft.IsEnabled = microsoft is not null; addSkin.IsEnabled = config?.SkinServers.Count > 0;
        mods.IsEnabled = !busy && !gameRunning;
        modSearch.IsEnabled = !busy;
        foreach (var control in editingControls) control.IsEnabled = !busy;
        routes.IsEnabled = !busy && config is not null;
        uninstall.IsEnabled = reinstallJava.IsEnabled = !busy && !gameRunning && game.HasGameFiles;
        foreach (var button in shortcuts.Children.OfType<Button>()) button.IsEnabled = !busy;
        UpdateAppearanceControls();
    }

    private void SetStatus(string message, bool error = false) => toast.Show(message, error);

    private async Task RefreshAsync()
    {
        config = null;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        request.CancelAfter(TimeSpan.FromSeconds(8));
        var url = new Uri(new Uri(LauncherDeployment.BackendUrl), "api/launcher");
        config = await http.GetFromJsonAsync<LauncherConfig>(url, request.Token) ?? throw new UserActionException("服务暂时不可用，请稍后重试。");
        installed = await packs.GetInstalledAsync(lifetime.Token);
        if (servers.ItemsSource is not IReadOnlyList<GameServer> previousServers || !SameServers(previousServers, config.Servers))
        {
            updatingServers = true;
            try
            {
                servers.ItemsSource = config.Servers;
                servers.SelectedItem = config.Servers.FirstOrDefault(s => s.Id == selectedServerId) ?? config.Servers.FirstOrDefault();
                selectedServerId = (servers.SelectedItem as GameServer)?.Id;
            }
            finally { updatingServers = false; }
        }
        ConfigureRoutes();
        RefreshHomeContent();
        await ConfigureMicrosoftAsync(); await SetActionAsync(); RefreshMods();
        _ = RefreshServerStatusAsync();
        await SavePreferencesAsync();
    }

    private async Task SetActionAsync() => actionText.Text = installed is null || !game.HasGameFiles ? "下载游戏" :
        config is not null && PackManager.IsCurrent(installed, config) && await game.IsPreparedAsync(installed) ? "启动游戏" : "更新游戏";

    private async Task ConfigureMicrosoftAsync()
    {
        if (microsoftClientId == config!.MicrosoftClientId) return;
        microsoftClientId = config.MicrosoftClientId; microsoft = null;
        var selectedKey = (accounts.SelectedItem as LauncherAccount)?.Key ?? preferences.SelectedAccount;
        foreach (var old in accountList.Where(a => a.MicrosoftId is not null))
        {
            var bitmap = old.Skin; old.Skin = null; bitmap?.Dispose();
        }
        accountList.RemoveAll(a => a.MicrosoftId is not null);
        UpdateAccounts(selectedKey);
        if (!string.IsNullOrWhiteSpace(microsoftClientId))
        {
            var cache = Path.Combine(root, "accounts.json");
            var store = new FileAccountStore(Path.Combine(root, "account-list.json"), cache);
            microsoft = new MicrosoftAuthService(new SerilogLauncherLogger(Path.Combine(root, "logs")), cache, store, microsoftClientId);
            microsoft.DeviceCodeRequested += (_, e) => Dispatcher.UIThread.Post(() => toast.Show($"请访问 {e.VerificationUrl}\n输入代码：{e.UserCode}", persistent: true));
            foreach (var account in await microsoft.ListCachedAsync(lifetime.Token))
                accountList.Add(new LauncherAccount(account.Username, account.Id, null, null, null, account.Uuid));
        }
        UpdateAccounts(selectedKey);
    }

    private async Task AddMicrosoftAsync()
    {
        toast.Show("正在登录微软账号", persistent: true);
        var session = await microsoft!.SignInInteractiveAsync(lifetime.Token);
        var account = (await microsoft.ListCachedAsync(lifetime.Token)).First(a => a.Uuid == session.Uuid);
        ReplaceAccount(new LauncherAccount(session.Username, account.Id, null, null, null, session.Uuid));
        SetStatus("微软账号已添加"); await SavePreferencesAsync();
    }

    private async Task AddSkinAsync()
    {
        var credentials = await AskSkinLoginAsync(config!.SkinServers);
        if (credentials is null) return;
        toast.Show("正在登录皮肤站", persistent: true);
        var credential = await SkinAuthentication.SignInAsync(skinHttp, credentials.Server.Url, credentials.Username, credentials.Password, lifetime.Token);
        await skinCredentials.WriteAsync(credentials.Server.Url, credentials.Username, credential, lifetime.Token);
        var session = credential.Account;
        ReplaceAccount(new LauncherAccount(session.Username, null, credentials.Server, session, credentials.Username, session.Uuid, credential.ClientToken));
        SaveSkinAccounts(); SetStatus("皮肤站账号已添加"); await SavePreferencesAsync();
    }

    private void ReplaceAccount(LauncherAccount account)
    {
        var previous = accountList.FirstOrDefault(a => a.Key == account.Key);
        if (previous is not null) { accountList.Remove(previous); var old = previous.Skin; previous.Skin = null; old?.Dispose(); }
        accountList.Add(account); UpdateAccounts(account.Key);
    }

    private void UpdateAccounts(string? selectedKey = null)
    {
        selectedKey ??= (accounts.SelectedItem as LauncherAccount)?.Key;
        accounts.ItemsSource = accountList.ToArray();
        accounts.SelectedItem = accountList.FirstOrDefault(a => a.Key == selectedKey) ?? accountList.FirstOrDefault();
    }

    private async Task LoadSelectedAvatarAsync()
    {
        avatarCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        avatarCancellation = cancellation;
        var account = accounts.SelectedItem as LauncherAccount;
        if (account is null || account.Skin is not null) { avatarCancellation = null; cancellation.Dispose(); return; }
        try
        {
            var bitmap = await skins.LoadAsync(account.Uuid, account.SkinServer?.Url, cancellation.Token);
            if (cancellation.IsCancellationRequested) bitmap?.Dispose();
            else account.Skin = bitmap;
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(avatarCancellation, cancellation)) avatarCancellation = null; cancellation.Dispose(); }
    }

    private async Task PrimaryAsync()
    {
        await RefreshAsync();
        if (!game.HasGameFiles || !PackManager.IsCurrent(installed, config!) || !await game.IsPreparedAsync(installed))
        {
            toast.BeginProgress("正在准备游戏");
            if (!PackManager.IsCurrent(installed, config!))
            {
                var highestProgress = 0d;
                var progress = new Progress<double>(p => { highestProgress = Math.Max(highestProgress, p); toast.ShowProgress($"正在准备游戏 {highestProgress:P0}", highestProgress); });
                installed = await packs.InstallAsync(config!, progress, lifetime.Token);
            }
            var gameProgress = new Progress<LaunchProgress>(p =>
            {
                var stage = p.Stage == "下载 Minecraft" || p.Stage.StartsWith("Downloading", StringComparison.Ordinal) ? "正在下载游戏" : "正在准备游戏";
                toast.ShowProgress(p.Fraction is double value ? $"{stage}：{value:P0}" : stage, p.Fraction);
            });
            await game.PrepareAsync(installed!, null, gameProgress, lifetime.Token);
            await SetActionAsync(); RefreshMods(); SetStatus("游戏已准备好"); return;
        }
        if (servers.SelectedItem is not GameServer server) throw new UserActionException("请选择服务器。");
        if (routes.SelectedItem is not RouteOption route) throw new UserActionException("请选择连接线路。");
        if (accounts.SelectedItem is not LauncherAccount selected) throw new UserActionException("请先添加账号。");
        toast.BeginProgress("正在启动游戏");
        AuthResult session;
        IReadOnlyList<string> jvm = [];
        if (selected.MicrosoftId is not null) session = await microsoft!.SignInSilentlyAsync(selected.MicrosoftId, lifetime.Token);
        else
        {
            if (!config!.SkinServers.Any(s => s.Id == selected.SkinServer!.Id && s.Url == selected.SkinServer.Url)) throw new UserActionException("此账号暂时无法使用，请添加其他账号。");
            var saved = selected.Session is not null && selected.ClientToken is not null
                ? new SkinSession(selected.Session, selected.ClientToken) : await ReadSkinCredentialAsync(selected);
            var credential = saved is null ? null : await SkinAuthentication.ResumeAsync(skinHttp, selected.SkinServer!.Url, saved, lifetime.Token);
            if (saved is not null && credential is null)
            {
                await skinCredentials.DeleteAsync(selected.SkinServer!.Url, selected.Login!, selected.Uuid!, lifetime.Token);
                ReplaceAccount(new LauncherAccount(selected.Name, null, selected.SkinServer, null, selected.Login, selected.Uuid));
            }
            var login = selected.Login!;
            if (credential is null)
            {
                var credentials = await AskSkinLoginAsync([selected.SkinServer!], selected.Login);
                if (credentials is null) { SetStatus("已取消登录"); return; }
                toast.BeginProgress("正在启动游戏");
                credential = await SkinAuthentication.SignInAsync(skinHttp, credentials.Server.Url, credentials.Username, credentials.Password, lifetime.Token);
                login = credentials.Username;
            }
            session = credential.Account;
            if (selected.Uuid is not null && session.Uuid != selected.Uuid)
                throw new UserActionException("当前账号的角色已变更，请重新添加账号。");
            if (credential != saved) await skinCredentials.WriteAsync(selected.SkinServer!.Url, login, credential, lifetime.Token);
            ReplaceAccount(new LauncherAccount(session.Username, null, selected.SkinServer, session, login, session.Uuid, credential.ClientToken));
            SaveSkinAccounts();
            var injector = await SkinAuthentication.EnsureInjectorAsync(http, Path.Combine(root, "authlib"), lifetime.Token);
            jvm = ["-javaagent:" + injector + "=" + selected.SkinServer!.Url];
        }
        await SavePreferencesAsync(); toast.ShowProgress("正在启动游戏");
        var launched = await game.LaunchAsync(installed!, session, route.Route.Host, route.Route.Port, null, jvm, null, lifetime.Token, ReadGamePreferences());
        await WatchGameAsync(launched);
    }

    private void LoadPreferences()
    {
        var path = Path.Combine(root, "settings.json");
        if (!File.Exists(path)) return;
        try { preferences = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? preferences; }
        catch (JsonException) { SetStatus("外观设置无法读取，已使用默认配色。", true); }
    }

    private async Task SavePreferencesAsync()
    {
        await preferenceSaveLock.WaitAsync(lifetime.Token);
        try
        {
            preferences = CapturePreferences() with { Theme = theme.SelectedItem as string ?? "雾蓝", AutoPalette = autoPalette.IsChecked == true, SelectedServerId = selectedServerId, SelectedAccount = (accounts.SelectedItem as LauncherAccount)?.Key };
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "settings.json");
            var staged = path + ".tmp";
            await File.WriteAllTextAsync(staged, JsonSerializer.Serialize(preferences), lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            File.Move(staged, path, overwrite: true);
        }
        finally { preferenceSaveLock.Release(); }
    }

    private async Task<bool> TrySavePreferencesAsync()
    {
        try { await SavePreferencesAsync(); return true; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return false; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { logger.Error("Could not save launcher settings", e); toast.Show("设置未能保存，请重试。", true); return false; }
    }

    private void LoadBackground()
    {
        if (preferences.BackgroundFile is null) return;
        try
        {
            using var input = File.OpenRead(preferences.BackgroundFile);
            backgroundBitmap = Bitmap.DecodeToWidth(input, 1600, BitmapInterpolationMode.HighQuality);
            backgroundLabel.Text = "已设置";
        }
        catch (Exception e) when (e is IOException or ArgumentException)
        { preferences = preferences with { BackgroundFile = null }; SetStatus("背景图片无法读取，请重新选择。", true); }
    }

    private async Task ChooseBackgroundAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择背景图片", AllowMultiple = false, FileTypeFilter = [FilePickerFileTypes.ImageAll]
        });
        if (files.Count == 0) return;
        await using var input = await files[0].OpenReadAsync();
        using var data = new MemoryStream();
        await input.CopyToAsync(data, lifetime.Token);
        var bytes = data.ToArray();
        var bitmap = await Task.Run(() =>
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return Bitmap.DecodeToWidth(stream, 1600, BitmapInterpolationMode.HighQuality);
        }, lifetime.Token);
        var path = Path.Combine(root, "appearance", "background.image");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var staged = path + ".tmp";
            await File.WriteAllBytesAsync(staged, bytes, lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            File.Move(staged, path, overwrite: true);
        }
        catch { bitmap.Dispose(); throw; }
        backgroundImage.Source = null; backgroundBitmap?.Dispose(); backgroundBitmap = bitmap;
        preferences = preferences with { BackgroundFile = path }; backgroundLabel.Text = "已设置";
        ApplyAppearance(); await SavePreferencesAsync(); SetStatus("背景图片已保存");
    }

    private void LoadSkinAccounts()
    {
        var path = Path.Combine(root, "skin-accounts.json");
        if (!File.Exists(path)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<List<SavedSkinAccount>>(File.ReadAllText(path)) ?? [];
            accountList.AddRange(saved.Select(a => new LauncherAccount(a.Name, null, a.Server, null, a.Login, a.Uuid)));
        }
        catch (JsonException) { SetStatus("保存的皮肤站账号无法读取，请重新添加。", true); }
    }

    private async Task<SkinSession?> ReadSkinCredentialAsync(LauncherAccount account)
    {
        if (account.Uuid is null) return null;
        try { return await skinCredentials.ReadAsync(account.SkinServer!.Url, account.Login!, account.Uuid, lifetime.Token); }
        catch (JsonException e)
        {
            logger.Error("Could not read saved skin login state", e);
            await skinCredentials.DeleteAsync(account.SkinServer!.Url, account.Login!, account.Uuid, lifetime.Token);
            return null;
        }
    }

    private void SaveSkinAccounts()
    {
        var saved = accountList.Where(a => a.SkinServer is not null && a.Login is not null).Select(a => new SavedSkinAccount(a.Name, a.SkinServer!, a.Login!, a.Uuid)).ToArray();
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "skin-accounts.json"), JsonSerializer.Serialize(saved));
    }

    private void RefreshMods()
    {
        mods.Children.Clear();
        var directory = Path.Combine(root, "game", "mods");
        var paths = Directory.Exists(directory) ? Directory.EnumerateFiles(directory).Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)).Order().ToArray() : [];
        var matching = paths.Where(p => Path.GetFileName(p).Contains(modSearch.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
        modCount.Text = paths.Length == 0 ? "暂无 Mod" : matching.Length == 0 ? "没有找到 Mod" : $"{matching.Length} 个 Mod";
        foreach (var path in matching)
        {
            var disabled = path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var name = Path.GetFileName(disabled ? path[..^9] : path);
            var check = new CheckBox { IsChecked = !disabled, VerticalAlignment = VerticalAlignment.Center };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("12,6,*,6,Auto"), MinHeight = 28, Children = { new PixelIcon { Kind = PixelIconKind.File, Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }, check } };
            Grid.SetColumn(row.Children[1], 2); Grid.SetColumn(check, 4); ToolTip.SetTip(row, name);
            var separator = new Border { Child = row, BorderThickness = new Thickness(0, 0, 0, 1) };
            separator.Bind(Border.BorderBrushProperty, this.GetResourceObservable("PixelBorder"));
            check.IsCheckedChanged += (_, _) =>
            {
                var enabledPath = Path.Combine(directory, name); var disabledPath = enabledPath + ".disabled";
                try { File.Move(check.IsChecked == true ? disabledPath : enabledPath, check.IsChecked == true ? enabledPath : disabledPath); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { logger.Error("Could not change mod state", e); SetStatus("更改失败，请重试。", true); RefreshMods(); }
            };
            mods.Children.Add(separator);
        }
    }

    private static TextBlock Label(string text, params string[] classes)
    {
        var label = new TextBlock { Text = text };
        foreach (var value in classes) label.Classes.Add(value);
        return label;
    }

    private sealed record Settings(string Theme = "雾蓝", string? BackgroundFile = null, bool AutoPalette = true, string? SelectedServerId = null, string? SelectedAccount = null, GamePreferences? Game = null, string Accent = "主题默认", int BackgroundOpacity = 100, int BackgroundShade = 20, string TextSize = "标准", bool Animations = true, bool RememberWindow = true, string OnGameStart = "保持窗口", double WindowWidth = 1280, double WindowHeight = 760, string StartupPage = "游玩", Dictionary<string, string>? SelectedRoutes = null);
    private sealed record SavedSkinAccount(string Name, SkinServer Server, string Login, string? Uuid = null);
    private sealed class UserActionException(string message) : Exception(message);
    private sealed record LauncherAccount(string Name, string? MicrosoftId, SkinServer? SkinServer, AuthResult? Session, string? Login, string? Uuid, string? ClientToken = null) : INotifyPropertyChanged
    {
        private Bitmap? skin;
        public string Key => MicrosoftId is not null ? "microsoft:" + MicrosoftId : "skin:" + SkinServer!.Url + ":" + Login;
        public string Provider => MicrosoftId is not null ? "Microsoft" : SkinServer!.Name;
        public Bitmap? Skin { get => skin; set { skin = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Skin))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
