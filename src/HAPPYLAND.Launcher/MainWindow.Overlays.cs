using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Happyland.Launcher.Appearance;

namespace Happyland.Launcher;

public sealed partial class MainWindow
{
    private readonly Grid overlayLayer = new() { IsVisible = false, Margin = new Thickness(0, 32, 0, 0), ZIndex = 10 };
    private Action? cancelOverlay;
    private Control? restoreOverlayFocus;

    private async Task<T> ShowOverlayAsync<T>(Control content, string title, Action cancel, Task<T> result, Action focus, bool scrollContent = true)
    {
        var previousFocus = FocusManager?.GetFocusedElement();
        cancelOverlay = cancel;
        var close = new Button { Width = 24, Height = 24, MinHeight = 0, Padding = new Thickness(0),
            Content = new PixelIcon { Kind = PixelIconKind.Close, Width = 12, Height = 12 }, Classes = { "quiet" } };
        close.Click += (_, _) => cancel();
        Avalonia.Automation.AutomationProperties.SetName(close, "关闭");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 4, 6, 4),
            Children = { new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }, close } };
        Grid.SetColumn(close, 1);
        Control body = scrollContent ? new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } : content;
        var originalMaxHeight = content.MaxHeight;
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { header, body } };
        Grid.SetRow(body, 1);
        KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);
        var frame = new PixelWindowChrome { Child = panel, Width = 380, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        frame.Bind(PixelWindowChrome.BackgroundProperty, this.GetResourceObservable("PixelPanel"));
        frame.Bind(PixelWindowChrome.BorderBrushProperty, this.GetResourceObservable("PixelBorder"));
        void FitPanel()
        {
            frame.MaxWidth = Math.Max(0, overlayLayer.Bounds.Width - 32);
            frame.MaxHeight = Math.Max(0, overlayLayer.Bounds.Height - 32);
            if (!scrollContent) content.MaxHeight = Math.Min(originalMaxHeight, Math.Max(0, frame.MaxHeight - 34));
        }
        void OnSizeChanged(object? sender, SizeChangedEventArgs e) => FitPanel();
        void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { cancel(); e.Handled = true; }
        }
        overlayLayer.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)) });
        overlayLayer.Children.Add(frame);
        overlayLayer.IsVisible = true;
        bodySurface.IsEnabled = false;
        overlayLayer.SizeChanged += OnSizeChanged;
        overlayLayer.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        FitPanel();
        Dispatcher.UIThread.Post(focus, DispatcherPriority.Loaded);
        try { return await result; }
        finally
        {
            overlayLayer.SizeChanged -= OnSizeChanged;
            overlayLayer.RemoveHandler(KeyDownEvent, OnKeyDown);
            overlayLayer.IsVisible = false;
            overlayLayer.Children.Clear();
            cancelOverlay = null;
            bodySurface.IsEnabled = true;
            toast.Hide();
            restoreOverlayFocus = previousFocus as Control ?? action;
        }
    }

    private void RestoreOverlayFocus()
    {
        var target = restoreOverlayFocus;
        restoreOverlayFocus = null;
        if (target is null || lifetime.IsCancellationRequested) return;
        if (target.IsVisible && target.IsEnabled && target.GetVisualRoot() == this) target.Focus();
        else if (addAccount.IsEnabled && addAccount.GetVisualRoot() == this) addAccount.Focus();
        else navigation.First(pair => pair.Value.Classes.Contains("selected")).Value.Focus();
    }

    private Task<SkinLogin?> AskSkinLoginAsync(IReadOnlyList<Happyland.Contracts.SkinServer> sites, string? username = null)
    {
        var login = new SkinLoginView(sites, username);
        login.ValidationError += message => toast.Show(message, true);
        return ShowOverlayAsync(login, "登录皮肤站", login.Cancel, login.Result, login.FocusFirstField);
    }
}
