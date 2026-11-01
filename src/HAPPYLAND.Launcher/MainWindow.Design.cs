using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Happyland.Contracts;
using Happyland.Launcher.Appearance;
using Happyland.Launcher.Core;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Ping;

namespace Happyland.Launcher;

public sealed partial class MainWindow
{
    private const double DefaultWindowWidth = 640;
    private const double DefaultWindowHeight = 380;
    private readonly TextBlock serverName = Label("AssiahLand", "hero");
    private readonly TextBlock serverPlayers = Label("—", "stat");
    private readonly TextBlock serverCapacity = Label("", "small", "muted");
    private readonly TextBlock serverLatency = Label("—", "stat");
    private readonly TextBox modSearch = new() { Watermark = "搜索 Mod" };
    private readonly ComboBox accentChoice = Choice(["主题默认", "薄荷", "琥珀", "鸢尾", "珊瑚"]);
    private readonly ComboBox textSize = Choice(["紧凑", "标准", "宽松"]);
    private readonly ComboBox interfaceZoom = Choice(["100%", "125%", "150%"]);
    private int SelectedInterfaceScalePercent => interfaceZoom.SelectedIndex switch { 0 => 100, 1 => 125, _ => 150 };
    private readonly CheckBox animatePages = new() { Content = "页面动画" };
    private readonly CheckBox rememberWindow = new() { Content = "记住窗口大小" };
    private readonly ComboBox afterLaunch = Choice(["保持窗口", "最小化窗口"]);
    private readonly ComboBox startupPage = Choice(["游玩", "Mod 管理", "游戏选项", "外观"]);
    private readonly NumericUpDown backgroundOpacity = Number(0, 100, 5);
    private readonly NumericUpDown backgroundShade = Number(0, 80, 5);
    private readonly Border backgroundDim = new() { Background = Brushes.Black, IsHitTestVisible = false };
    private readonly MemoryEditor memory = new();
    private readonly ResolutionEditor resolution = new();
    private readonly ComboBox frameLimit = Choice(["60", "120", "144", "165", "240", "不限"]);
    private readonly ComboBox guiSize = Choice(["自动", "2", "3", "4"]);
    private readonly NumericUpDown viewDistance = Number(4, 32, 1);
    private readonly NumericUpDown masterVolume = Number(0, 100, 5);
    private readonly NumericUpDown musicVolume = Number(0, 100, 5);
    private readonly NumericUpDown brightness = Number(0, 100, 5);
    private readonly CheckBox fullScreen = new() { Content = "全屏启动" };
    private readonly CheckBox verticalSync = new() { Content = "垂直同步" };
    private readonly DispatcherTimer serverTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly TcpServerPinger pinger = new();
    private CancellationTokenSource? pingCancellation;
    private Process? runningGame;
    private bool gameRunning;
    private readonly Grid bodySurface = new() { ColumnDefinitions = new ColumnDefinitions("76,*") };

    private void InitializeOptions()
    {
        var gameOptions = preferences.Game ?? new GamePreferences();
        memory.Value = gameOptions.MaximumMemoryMb;
        resolution.WidthValue = gameOptions.Width;
        resolution.HeightValue = gameOptions.Height;
        frameLimit.SelectedItem = gameOptions.MaximumFps >= 260 ? "不限" : gameOptions.MaximumFps.ToString(CultureInfo.InvariantCulture);
        if (frameLimit.SelectedIndex < 0) frameLimit.SelectedItem = "120";
        guiSize.SelectedItem = gameOptions.GuiScale == 0 ? "自动" : gameOptions.GuiScale.ToString(CultureInfo.InvariantCulture);
        if (guiSize.SelectedIndex < 0) guiSize.SelectedIndex = 0;
        viewDistance.Value = gameOptions.RenderDistance; masterVolume.Value = gameOptions.MasterVolume;
        musicVolume.Value = gameOptions.MusicVolume; brightness.Value = gameOptions.Brightness;
        fullScreen.IsChecked = gameOptions.FullScreen; verticalSync.IsChecked = gameOptions.VSync;
        accentChoice.SelectedItem = preferences.Accent; textSize.SelectedItem = preferences.TextSize;
        if (accentChoice.SelectedIndex < 0) accentChoice.SelectedIndex = 0;
        if (textSize.SelectedIndex < 0) textSize.SelectedIndex = 1;
        interfaceZoom.SelectedIndex = preferences.InterfaceScalePercent switch { 100 => 0, 125 => 1, _ => 2 };
        backgroundOpacity.Value = preferences.BackgroundOpacity; backgroundShade.Value = preferences.BackgroundShade;
        animatePages.IsChecked = preferences.Animations; rememberWindow.IsChecked = preferences.RememberWindow;
        afterLaunch.SelectedItem = preferences.OnGameStart;
        if (afterLaunch.SelectedIndex < 0) afterLaunch.SelectedIndex = 0;
        startupPage.SelectedItem = preferences.StartupPage;
        if (startupPage.SelectedIndex < 0) startupPage.SelectedIndex = 0;
    }

    private void BuildInterface()
    {
        accounts.MinHeight = 0;
        accounts.VerticalAlignment = VerticalAlignment.Stretch;
        accounts.ItemTemplate = new FuncDataTemplate<LauncherAccount>((account, _) =>
        {
            if (account is null) return null;
            var face = new SkinHeadView { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
            face.Bind(SkinHeadView.SkinProperty, new Binding(nameof(LauncherAccount.Skin)));
            var name = new TextBlock { Text = account.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var item = new Grid { ColumnDefinitions = new ColumnDefinitions("16,8,*"), Children = { face, name } };
            Grid.SetColumn(name, 2);
            ToolTip.SetTip(item, account.Name + "\n" + account.Provider);
            return item;
        });
        action.Height = 38; action.Classes.Add("primary");
        actionText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("PixelAccentText"));
        action.Content = actionText;
        var accountRow = new Grid { Height = 36, ColumnDefinitions = new ColumnDefinitions("*,6,28"), Children = { accounts, addAccount } };
        Grid.SetColumn(addAccount, 2);
        ToolTip.SetTip(addAccount, "添加账号");
        var launchPanel = new StackPanel { Spacing = 9, Children =
        {
            Label("使用账号", "small", "muted"), accountRow, action,
            Label("服务器", "small", "muted"), servers
        } };
        var stats = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,*") };
        serverCapacity.VerticalAlignment = VerticalAlignment.Bottom;
        serverCapacity.Margin = new Thickness(3, 0, 0, 3);
        var playerCount = new StackPanel { Orientation = Orientation.Horizontal, Children = { serverPlayers, serverCapacity } };
        stats.Children.Add(new StackPanel { Spacing = 7, Children = { Label("在线人数", "small", "muted"), playerCount } });
        var latency = new StackPanel { Spacing = 7, Children = { Label("连接延迟", "small", "muted"), serverLatency } }; Grid.SetColumn(latency, 2); stats.Children.Add(latency);
        homeNoticeArea.Content = homeNotice;
        var serverPanel = new StackPanel { Spacing = 14, Children = { serverName, stats, routes, homeNoticeArea } };
        var homeColumns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,180"), VerticalAlignment = VerticalAlignment.Top };
        homeColumns.Children.Add(Card(serverPanel));
        var launchCard = Card(launchPanel); Grid.SetColumn(launchCard, 2); homeColumns.Children.Add(launchCard);
        var homeContent = Page("开始游戏", homeColumns);
        var shortcutBar = BuildShortcutBar();
        homePage = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Children = { homeContent, shortcutBar } };
        Grid.SetRow(shortcutBar, 1);
        modSearch.TextChanged += (_, _) => RefreshMods();
        modsPage = Page("Mod 管理", new StackPanel { Spacing = 14, Children = { modSearch, modCount, Card(mods) } });
        settingsPage = BuildSettings(); gameSettingsPage = BuildGameSettings();
        var sidebar = BuildSidebar();
        var body = bodySurface;
        body.Children.Add(sidebar);
        var content = new Grid { ClipToBounds = true, Children = { page } };
        Grid.SetColumn(content, 1); body.Children.Add(content);
        var shell = new Grid { RowDefinitions = new RowDefinitions("32,*"), Children = { BuildHeader(), body } };
        Grid.SetRow(body, 1);
        viewport.Children.Add(backgroundImage); viewport.Children.Add(backgroundDim);
        viewport.Children.Add(shell); viewport.Children.Add(overlayLayer); viewport.Children.Add(toast);
        Content = BuildWindowFrame();
        WireDesignActions(); ShowPage(preferences.StartupPage switch { "外观" => "设置", "Mod 管理" => "Mod 管理", "游戏选项" => "游戏选项", _ => "游戏" });
    }

    private Control BuildHeader()
    {
        using var stream = AssetLoader.Open(new Uri("avares://HAPPYLAND.Launcher/Assets/server-icon.png"));
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
            Children = { new Image { Source = Bitmap.DecodeToWidth(stream, 18, BitmapInterpolationMode.None), Width = 18, Height = 18 }, Label("HAPPYLAND Launcher", "brand") } };
        Button WindowButton(PixelIconKind kind, string name)
        {
            var button = new Button { Width = 26, Height = 26, MinHeight = 0, Padding = new Thickness(0), Content = new PixelIcon { Kind = kind, Width = 12, Height = 12 }, Classes = { "quiet" } };
            ToolTip.SetTip(button, name);
            Avalonia.Automation.AutomationProperties.SetName(button, name);
            return button;
        }
        var minimize = WindowButton(PixelIconKind.Minimize, "最小化");
        var maximize = WindowButton(PixelIconKind.Maximize, "最大化");
        var close = WindowButton(PixelIconKind.Close, "关闭");
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                ((PixelIcon)maximize.Content!).Kind = WindowState == WindowState.Maximized ? PixelIconKind.Restore : PixelIconKind.Maximize;
                ToolTip.SetTip(maximize, WindowState == WindowState.Maximized ? "还原" : "最大化");
            }
        };
        minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        maximize.Click += (_, _) => ToggleMaximize(); close.Click += (_, _) => Close();
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { minimize, maximize, close } };
        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 0, 6, 0), Children = { brand, controls } };
        Grid.SetColumn(controls, 1);
        var header = new Border { Child = title, BorderThickness = new Thickness(0, 0, 0, 1) };
        header.Bind(Border.BackgroundProperty, this.GetResourceObservable("PixelHeader")); header.Bind(Border.BorderBrushProperty, this.GetResourceObservable("PixelBorder"));
        header.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.Source is Visual source && (source is Button || source.GetVisualAncestors().OfType<Button>().Any())) return;
            if (e.ClickCount == 2) ToggleMaximize(); else BeginMoveDrag(e);
            e.Handled = true;
        };
        return header;
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private Control BuildSidebar()
    {
        var buttons = new StackPanel { Spacing = 6, Margin = new Thickness(8, 16, 8, 12) };
        foreach (var (key, text, icon) in new[] { ("游戏", "游玩", PixelIconKind.Play), ("Mod 管理", "Mod", PixelIconKind.File), ("游戏选项", "游戏选项", PixelIconKind.Gear), ("设置", "外观", PixelIconKind.Palette) })
        {
            var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 47, Classes = { "sidebar" }, Content =
                new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, Children = { new PixelIcon { Kind = icon, Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Center }, Label(text, "small") } } };
            button.Click += (_, _) => ShowPage(key); navigation[key] = button; buttons.Children.Add(button);
        }
        var version = GetType().Assembly.GetName().Version!.ToString(3);
        var label = Label("v" + version, "small", "muted");
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.HorizontalAlignment = HorizontalAlignment.Center; label.Margin = new Thickness(8, 0, 8, 12);
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Children = { buttons, label } };
        Grid.SetRow(label, 1);
        var border = new Border { Child = layout, BorderThickness = new Thickness(0, 0, 1, 0) };
        border.Bind(Border.BackgroundProperty, this.GetResourceObservable("PixelHeader")); border.Bind(Border.BorderBrushProperty, this.GetResourceObservable("PixelBorder"));
        return border;
    }

    private Control BuildSettings()
    {
        var gallery = BuildBackgroundGallery();
        var save = SaveButton();
        editingControls.AddRange([theme, accentChoice, textSize, interfaceZoom, backgroundOpacity, backgroundShade, autoPalette, animatePages, rememberWindow, afterLaunch, startupPage, save]);
        return Page("启动器外观", new StackPanel { Spacing = 14, Children =
        {
            Section("界面", Field("界面缩放", interfaceZoom), Field("字号", textSize), Field("预设主题", theme), Field("强调色", accentChoice)),
            Section("背景", gallery, autoPalette,
                Field("图片强度 %", backgroundOpacity), Field("暗化程度 %", backgroundShade)),
            Section("使用习惯", animatePages, rememberWindow, Field("启动页面", startupPage), Field("启动游戏后", afterLaunch)), save
        } });
    }

    private Control BuildGameSettings()
    {
        var save = SaveButton();
        uninstall.HorizontalAlignment = reinstallJava.HorizontalAlignment = save.HorizontalAlignment = HorizontalAlignment.Stretch;
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,8,Auto,8,Auto"), HorizontalAlignment = HorizontalAlignment.Right, Children = { uninstall, reinstallJava, save } };
        Grid.SetColumn(reinstallJava, 2); Grid.SetColumn(save, 4);
        editingControls.AddRange([memory, resolution, frameLimit, guiSize, viewDistance, masterVolume, musicVolume, brightness, fullScreen, verticalSync, save]);
        return Page("游戏选项", new StackPanel { Spacing = 14, Children =
        {
            Section("启动", Field("游戏内存", memory), Field("窗口大小", resolution), fullScreen),
            Section("画面", Field("视距 / 区块", viewDistance), Field("帧率上限", frameLimit), verticalSync, Field("界面大小", guiSize), Field("亮度 %", brightness)),
            Section("声音", Field("主音量 %", masterVolume), Field("音乐音量 %", musicVolume)), actions
        } });
    }

    private Button SaveButton()
    {
        var save = new Button { Content = "保存设置", HorizontalAlignment = HorizontalAlignment.Right, Classes = { "primary" } };
        save.Click += async (_, _) => { ApplyAppearance(); if (await TrySavePreferencesAsync()) SetStatus("已保存"); };
        return save;
    }

    private Control Page(string heading, Control content) => new ScrollViewer
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        Content = new StackPanel { Spacing = 12, Margin = new Thickness(18, 8, 18, 12), Children = { Label(heading, "title"), content } }
    };
    private Border Card(Control content)
    {
        var card = new Border { Padding = new Thickness(13), BorderThickness = new Thickness(1), Child = content };
        card.Bind(Border.BackgroundProperty, this.GetResourceObservable("PixelPanel")); card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("PixelBorder"));
        return card;
    }
    private Control Section(string title, params Control[] controls)
    {
        var content = new StackPanel { Spacing = 10 }; content.Children.Add(Label(title, "section-title"));
        foreach (var control in controls) content.Children.Add(control);
        var section = Card(content);
        section.Padding = new Thickness(13, 8, 13, 12);
        return section;
    }
    private Control Field(string title, Control control)
    {
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0) };
        if (control is NumericUpDown number)
        {
            number.ShowButtonSpinner = false;
            number.BorderThickness = new Thickness(0);
            var minus = new Button { Content = "-", Width = 24, BorderThickness = new Thickness(0), Classes = { "quiet" } };
            var plus = new Button { Content = "+", Width = 24, BorderThickness = new Thickness(0), Classes = { "quiet" } };
            minus.Click += (_, _) => number.Value = Math.Max(number.Minimum, (number.Value ?? number.Minimum) - number.Increment);
            plus.Click += (_, _) => number.Value = Math.Min(number.Maximum, (number.Value ?? number.Minimum) + number.Increment);
            editingControls.AddRange([minus, plus]);
            var cells = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,24"), Children = { minus, number, plus } };
            Grid.SetColumn(number, 1); Grid.SetColumn(plus, 2);
            var editor = new Border { BorderThickness = new Thickness(1), Child = cells };
            editor.Bind(Border.BackgroundProperty, this.GetResourceObservable("PixelPanel"));
            editor.Bind(Border.BorderBrushProperty, this.GetResourceObservable("PixelBorder"));
            control = editor;
        }
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(control is MemoryEditor or ResolutionEditor ? "*,240" : "*,140"), Children = { label, control } }; Grid.SetColumn(control, 1); return row;
    }
    private static ComboBox Choice(string[] items) => new() { ItemsSource = items, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static NumericUpDown Number(decimal min, decimal max, decimal step) => new() { Minimum = min, Maximum = max, Increment = step, FormatString = "0", HorizontalAlignment = HorizontalAlignment.Stretch };

    private void WireDesignActions()
    {
        serverTimer.Tick += async (_, _) => await RefreshServerStatusAsync();
        animatePages.IsCheckedChanged += (_, _) => ApplyMotion();
        accentChoice.SelectionChanged += (_, _) => { if (!initializing) ApplyAppearance(); };
        textSize.SelectionChanged += (_, _) => { if (!initializing) ApplyAppearance(); };
        interfaceZoom.SelectionChanged += async (_, _) =>
        {
            UpdateWindowScale(resizeWindow: true);
            await TrySavePreferencesAsync();
        };
        backgroundOpacity.ValueChanged += async (_, _) =>
        {
            backgroundImage.Opacity = (double)(backgroundOpacity.Value ?? 60) / 100;
            await SaveBackgroundAdjustmentAsync();
        };
        backgroundShade.ValueChanged += async (_, _) =>
        {
            backgroundDim.Opacity = (double)(backgroundShade.Value ?? 30) / 100;
            await SaveBackgroundAdjustmentAsync();
        };
    }
    private void ApplyMotion()
    {
        var enabled = animatePages.IsChecked == true;
        page.PageTransition = enabled ? new CrossFade(TimeSpan.FromMilliseconds(170)) : null;
        toast.AnimationsEnabled = enabled;
    }
    private void ApplyAppearance()
    {
        PixelTheme.Apply(Application.Current!, theme.SelectedItem as string ?? "雾蓝", autoPalette.IsChecked == true ? backgroundBitmap : null, accentChoice.SelectedItem as string ?? "主题默认", backgroundBitmap is not null);
        PixelTypography.Apply(Application.Current!, textSize.SelectedItem as string ?? "标准");
        backgroundBrush.Source = backgroundBitmap;
        backgroundBrush.AlignmentY = BackgroundPreset.All.FirstOrDefault(item => item.Id == preferences.BackgroundPresetId)?.Alignment ?? AlignmentY.Center;
        backgroundImage.Opacity = (double)(backgroundOpacity.Value ?? 35) / 100;
        backgroundDim.IsVisible = backgroundBitmap is not null;
        backgroundDim.Opacity = (double)(backgroundShade.Value ?? 35) / 100;
        ApplyMotion();
        UpdateAppearanceControls();
    }

    private void UpdateAppearanceControls()
    {
        var followingBackground = backgroundBitmap is not null && autoPalette.IsChecked == true;
        theme.IsEnabled = accentChoice.IsEnabled = !busy && !followingBackground;
        autoPalette.IsEnabled = !busy && backgroundBitmap is not null;
        backgroundOpacity.IsEnabled = backgroundShade.IsEnabled = !busy && backgroundBitmap is not null;
    }

    private Settings CapturePreferences() => preferences with
    {
        Game = ReadGamePreferences(), Accent = accentChoice.SelectedItem as string ?? "主题默认", TextSize = textSize.SelectedItem as string ?? "标准",
        BackgroundOpacity = (int)(backgroundOpacity.Value ?? 35), BackgroundShade = (int)(backgroundShade.Value ?? 35), Animations = animatePages.IsChecked == true,
        RememberWindow = rememberWindow.IsChecked == true, OnGameStart = afterLaunch.SelectedItem as string ?? "保持窗口", StartupPage = startupPage.SelectedItem as string ?? "游玩",
        SelectedRoutes = new Dictionary<string, string>(selectedRoutes, StringComparer.Ordinal),
        InterfaceScalePercent = SelectedInterfaceScalePercent,
        CustomBackgrounds = customBackgrounds.ToList(),
        BackgroundAdjustments = new Dictionary<string, BackgroundAdjustment>(backgroundAdjustments, StringComparer.Ordinal)
    };
    private GamePreferences ReadGamePreferences()
    {
        return new GamePreferences
        {
            MaximumMemoryMb = memory.Value, Width = resolution.WidthValue, Height = resolution.HeightValue,
            FullScreen = fullScreen.IsChecked == true, RenderDistance = (int)(viewDistance.Value ?? 12),
            MaximumFps = frameLimit.SelectedItem as string == "不限" ? 260 : int.Parse((string)frameLimit.SelectedItem!, CultureInfo.InvariantCulture),
            VSync = verticalSync.IsChecked == true, MasterVolume = (int)(masterVolume.Value ?? 80), MusicVolume = (int)(musicVolume.Value ?? 50),
            Brightness = (int)(brightness.Value ?? 50), GuiScale = guiSize.SelectedIndex == 0 ? 0 : int.Parse((string)guiSize.SelectedItem!, CultureInfo.InvariantCulture)
        };
    }

    private Screen CurrentScreen() => Screens.ScreenFromWindow(this) ?? Screens.Primary!;

    private void UpdateWindowScale(bool resizeWindow)
    {
        var screen = CurrentScreen();
        var area = screen.WorkingArea;
        var available = area.Size.ToSize(screen.Scaling);
        var previousScale = interfaceScale;
        var previousSize = ClientSize;
        var previousPosition = Position;
        var scale = Math.Min(SelectedInterfaceScalePercent / 100d, Math.Min(available.Width / 470, available.Height / 310));
        if (interfaceViewport.LayoutTransform is null || interfaceScale != scale)
        {
            interfaceScale = scale;
            interfaceViewport.LayoutTransform = new ScaleTransform(scale, scale);
            chrome.PixelUnit = scale;
        }
        MinWidth = Math.Min(470 * scale, available.Width);
        MinHeight = Math.Min(310 * scale, available.Height);
        MaxWidth = available.Width;
        MaxHeight = available.Height;
        if (resizeWindow && previousScale != scale && WindowState == WindowState.Normal)
        {
            var width = Math.Clamp(previousSize.Width * scale / previousScale, MinWidth, MaxWidth);
            var height = Math.Clamp(previousSize.Height * scale / previousScale, MinHeight, MaxHeight);
            var physicalWidth = (int)Math.Round(width * screen.Scaling);
            var physicalHeight = (int)Math.Round(height * screen.Scaling);
            var x = (int)Math.Round(previousPosition.X + (previousSize.Width - width) * screen.Scaling / 2);
            var y = (int)Math.Round(previousPosition.Y + (previousSize.Height - height) * screen.Scaling / 2);
            Width = width;
            Height = height;
            Position = new PixelPoint(Math.Clamp(x, area.X, area.Right - physicalWidth), Math.Clamp(y, area.Y, area.Bottom - physicalHeight));
        }
    }

    private void InitializeWindowSize()
    {
        UpdateWindowScale(resizeWindow: false);
        var width = preferences.RememberWindow ? preferences.WindowWidth : DefaultWindowWidth;
        var height = preferences.RememberWindow ? preferences.WindowHeight : DefaultWindowHeight;
        Width = Math.Clamp(width * interfaceScale, MinWidth, MaxWidth);
        Height = Math.Clamp(height * interfaceScale, MinHeight, MaxHeight);
        ScalingChanged += (_, _) => UpdateWindowScale(resizeWindow: false);
        PositionChanged += (_, _) => UpdateWindowScale(resizeWindow: false);
        Closing += (_, _) =>
        {
            try
            {
                preferences = CapturePreferences();
                if (WindowState == WindowState.Normal)
                    preferences = preferences with { WindowWidth = ClientSize.Width / interfaceScale, WindowHeight = ClientSize.Height / interfaceScale };
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "settings.json"), System.Text.Json.JsonSerializer.Serialize(preferences));
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { logger.Error("Could not save window settings", e); }
        };
    }

    private async Task RefreshServerStatusAsync()
    {
        pingCancellation?.Cancel();
        if (servers.SelectedItem is not GameServer server) return;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); pingCancellation = cancellation;
        serverName.Text = server.Name;
        try
        {
            await Task.WhenAll(routeOptions.Select(option => RefreshRouteAsync(option, cancellation.Token)));
        }
        finally { if (ReferenceEquals(pingCancellation, cancellation)) pingCancellation = null; cancellation.Dispose(); }
    }

    private void UpdateServerNumbers(ServerStatus? result)
    {
        var players = result?.OnlinePlayers.ToString(CultureInfo.InvariantCulture) ?? "—";
        var capacity = result is null ? "" : "/ " + result.MaxPlayers.ToString(CultureInfo.InvariantCulture);
        var latency = result is null ? "—" : result.LatencyMs + " ms";
        if (serverPlayers.Text != players) serverPlayers.Text = players;
        if (serverCapacity.Text != capacity) serverCapacity.Text = capacity;
        if (serverLatency.Text != latency) serverLatency.Text = latency;
    }

    private void OpenGameFolder()
    {
        var path = Path.Combine(root, "game"); Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    private async Task WatchGameAsync(GameLaunchResult launched)
    {
        var processId = launched.ProcessId;
        runningGame?.Dispose();
        runningGame = null;
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException) { throw new UserActionException("游戏在启动完成前退出，请重试。"); }
        runningGame = process;
        var minimizeOnReady = afterLaunch.SelectedIndex == 1;
        var autoMinimized = false;
        var restoreState = WindowState;
        var unconfirmedWindow = false;
        var startupReady = false;
        EventHandler<AvaloniaPropertyChangedEventArgs> windowChanged = (_, e) =>
        {
            if (e.Property == WindowStateProperty && WindowState != WindowState.Minimized) autoMinimized = false;
        };
        PropertyChanged += windowChanged;
        process.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            PropertyChanged -= windowChanged;
            if (lifetime.IsCancellationRequested || !ReferenceEquals(runningGame, process)) return;
            gameRunning = false;
            SetAvailability();
            if (autoMinimized && WindowState == WindowState.Minimized) { Show(); WindowState = restoreState; }
            if (unconfirmedWindow || startupReady && process.ExitCode != 0)
                SetStatus(process.ExitCode == 0 ? "游戏已退出" : "游戏异常退出，请重试。", process.ExitCode != 0);
        });
        gameRunning = true;
        try { process.EnableRaisingEvents = true; }
        catch (InvalidOperationException) when (process.HasExited)
        {
            PropertyChanged -= windowChanged;
            gameRunning = false;
            SetAvailability();
            throw new UserActionException("游戏在启动完成前退出，请重试。");
        }
        SetAvailability();
        var result = await launched.Startup.WaitAsync(lifetime.Token);
        lifetime.Token.ThrowIfCancellationRequested();
        logger.Info($"Game {processId}: startup state {result}.");
        process.Refresh();
        if (result == GameStartupStatus.Exited || process.HasExited)
        {
            gameRunning = false;
            SetAvailability();
            throw new UserActionException("游戏在启动完成前退出，请重试。");
        }
        if (result == GameStartupStatus.Ready)
        {
            startupReady = true;
            SetStatus("游戏已启动");
            if (minimizeOnReady && WindowState != WindowState.Minimized)
            {
                restoreState = WindowState;
                autoMinimized = true;
                WindowState = WindowState.Minimized;
            }
        }
        else
        {
            unconfirmedWindow = true;
            SetStatus("游戏启动时间较长，请稍候。");
        }
    }
}
