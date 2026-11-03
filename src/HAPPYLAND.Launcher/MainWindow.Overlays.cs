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
    private Action? focusOverlay;
    private Control? restoreOverlayFocus;

    private async Task<T> ShowOverlayAsync<T>(Control content, string title, Action cancel, Task<T> result, Action focus, bool scrollContent = true, double width = 380, Control? headerContent = null)
    {
        using var cancellation = lifetime.Token.Register(cancel);
        var previousFocus = FocusManager?.GetFocusedElement();
        var previousCancel = cancelOverlay;
        var previousOverlayFocus = focusOverlay;
        var previousLayer = overlayLayer.Children.LastOrDefault();
        cancelOverlay = cancel;
        focusOverlay = focus;
        var close = new Button { Width = 24, Height = 24, MinHeight = 0, Padding = new Thickness(0),
            Content = new PixelIcon { Kind = PixelIconKind.Close, Width = 12, Height = 12 }, Classes = { "quiet" } };
        close.Click += (_, _) => cancel();
        Avalonia.Automation.AutomationProperties.SetName(close, "关闭");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(12, 4, 6, 4),
            Children = { new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }, close } };
        Grid.SetColumn(close, 2);
        if (headerContent is not null) { Grid.SetColumn(headerContent, 1); header.Children.Add(headerContent); }
        Control body = scrollContent ? new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } : content;
        var originalMaxHeight = content.MaxHeight;
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { header, body } };
        Grid.SetRow(body, 1);
        KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);
        var frame = new PixelWindowChrome { Child = panel, Width = width, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
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
        var layer = new Grid { Children = { new Border { Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)) }, frame } };
        if (previousLayer is not null) previousLayer.IsEnabled = false;
        overlayLayer.Children.Add(layer);
        overlayLayer.IsVisible = true;
        bodySurface.IsEnabled = false;
        overlayLayer.SizeChanged += OnSizeChanged;
        layer.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        FitPanel();
        Dispatcher.UIThread.Post(focus, DispatcherPriority.Loaded);
        try { return await result; }
        finally
        {
            overlayLayer.SizeChanged -= OnSizeChanged;
            layer.RemoveHandler(KeyDownEvent, OnKeyDown);
            overlayLayer.Children.Remove(layer);
            if (previousLayer is not null) previousLayer.IsEnabled = true;
            overlayLayer.IsVisible = overlayLayer.Children.Count > 0;
            cancelOverlay = previousCancel;
            focusOverlay = previousOverlayFocus;
            bodySurface.IsEnabled = !overlayLayer.IsVisible;
            toast.Hide();
            restoreOverlayFocus = previousFocus as Control ?? action;
        }
    }

    private async Task ShowErrorDetailsAsync(string details)
    {
        var view = new ErrorDetailsView(details, async () =>
        {
            try
            {
                await Clipboard!.SetTextAsync(details);
                SetStatus("已复制");
            }
            catch (Exception e)
            {
                logger.Error("Could not copy error details", e);
                SetStatus("复制失败，请重试。", true, e.ToString());
            }
        });
        await ShowOverlayAsync(view, "错误详情", view.Close, view.Result, view.FocusFirstField, scrollContent: false);
        RestoreOverlayFocus();
    }

    private void RestoreOverlayFocus()
    {
        var target = restoreOverlayFocus;
        restoreOverlayFocus = null;
        if (target is null || lifetime.IsCancellationRequested) return;
        if (overlayLayer.IsVisible) { focusOverlay!(); return; }
        if (target.IsVisible && target.IsEnabled && target.GetVisualRoot() == this) target.Focus();
        else if (addAccount.IsEnabled && addAccount.GetVisualRoot() == this) addAccount.Focus();
        else navigation.First(pair => pair.Value.Classes.Contains("selected")).Value.Focus();
    }

    private Task<SkinLogin?> AskSkinLoginAsync(IReadOnlyList<Happyland.Contracts.SkinServer> sites, string? username = null)
    {
        var login = new SkinLoginView(sites, username);
        login.ValidationError += message => toast.Show(message, true);
        login.SiteRequested += async site =>
        {
            try { await OpenSkinSiteAsync(site); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception e) { logger.Error("Could not open skin site", e); SetStatus("皮肤站官网暂时无法打开。", true, e.ToString()); }
        };
        return ShowOverlayAsync(login, "登录皮肤站", login.Cancel, login.Result, login.FocusFirstField);
    }
}
