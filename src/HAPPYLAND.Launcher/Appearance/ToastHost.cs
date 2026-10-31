using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Happyland.Launcher.Appearance;

public sealed class ToastHost : Border
{
    private readonly SelectableTextBlock message = new()
    {
        FontFamily = new FontFamily("avares://HAPPYLAND.Launcher/Assets/Fonts#Fusion Pixel 10px Prop zh_hans"),
        FontSize = 10, LineHeight = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
    };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 1, MinWidth = 0, Height = 2, IsVisible = false };
    private readonly Canvas countdown = new() { Height = 1 };
    private readonly Border elapsedFill = new() { Height = 1 };
    private readonly Border accent = new() { Width = 2 };
    private readonly Button close;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch elapsed = new();
    private IDisposable accentBinding;
    private bool errorTone, persistent, pointerOver, progressActive, progressDismissed;

    public ToastHost()
    {
        MinWidth = 160;
        MaxWidth = 240;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(12, 44, 12, 12);
        BorderThickness = new Thickness(1);
        Opacity = 0;
        IsHitTestVisible = IsEnabled = false;
        ZIndex = 20;
        this.Bind(BackgroundProperty, this.GetResourceObservable("PixelPanel"));
        this.Bind(BorderBrushProperty, this.GetResourceObservable("PixelBorder"));
        message.Bind(SelectableTextBlock.ForegroundProperty, this.GetResourceObservable("PixelText"));
        message.Bind(SelectableTextBlock.SelectionBrushProperty, this.GetResourceObservable("PixelAccent"));
        message.Bind(SelectableTextBlock.SelectionForegroundBrushProperty, this.GetResourceObservable("PixelAccentText"));
        accentBinding = accent.Bind(BackgroundProperty, this.GetResourceObservable("PixelAccent"));
        countdown.Bind(BackgroundProperty, this.GetResourceObservable("PixelAccent"));
        elapsedFill.Bind(BackgroundProperty, this.GetResourceObservable("PixelBorder"));
        countdown.Children.Add(elapsedFill);
        close = new Button { Width = 14, Height = 14, MinHeight = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Top,
            Content = new PixelIcon { Kind = PixelIconKind.Close, Width = 8, Height = 8 }, Classes = { "quiet" } };
        AutomationProperties.SetName(close, "关闭提示");
        close.Click += (_, _) => Hide();
        var content = new StackPanel { Spacing = 4, Children = { message, progress } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,14"), Margin = new Thickness(8, 5), Children = { content, close } };
        Grid.SetColumn(close, 2);
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,1"), Children = { row, countdown } };
        Grid.SetRow(countdown, 1);
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("2,*"), Children = { accent, body } };
        Grid.SetColumn(body, 1);
        Child = layout;
        timer.Tick += (_, _) =>
        {
            var fraction = elapsed.Elapsed.TotalSeconds / 3;
            elapsedFill.Width = countdown.Bounds.Width * Math.Min(1, fraction);
            if (fraction >= 1) Hide();
        };
        PointerEntered += (_, _) => { pointerOver = true; elapsed.Stop(); timer.Stop(); };
        PointerExited += (_, _) => { pointerOver = false; ResumeCountdown(); };
        DetachedFromVisualTree += (_, _) => { elapsed.Stop(); timer.Stop(); };
        AnimationsEnabled = true;
    }

    public bool AnimationsEnabled
    {
        set => Transitions = value ? new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) } } : null;
    }

    public void Show(string text, bool error = false, bool persistent = false)
    {
        progressActive = false;
        progress.IsVisible = false;
        Present(text, error, persistent, dismissible: !persistent);
    }

    public void BeginProgress(string text)
    {
        progressActive = true;
        progressDismissed = false;
        ShowProgress(text);
    }

    public void ShowProgress(string text, double? fraction = null)
    {
        if (!progressActive) return;
        progress.IsVisible = true;
        progress.IsIndeterminate = !fraction.HasValue;
        if (fraction is double value) progress.Value = value;
        if (!progressDismissed) Present(text, false, true, dismissible: true);
    }

    public void Hide()
    {
        if (progressActive) progressDismissed = true;
        timer.Stop();
        elapsed.Stop();
        Opacity = 0;
        IsHitTestVisible = IsEnabled = false;
    }

    private void Present(string text, bool error, bool keepOpen, bool dismissible)
    {
        message.Text = text;
        persistent = keepOpen;
        close.IsVisible = dismissible;
        countdown.IsVisible = !persistent;
        elapsed.Reset();
        elapsedFill.Width = 0;
        timer.Stop();
        if (errorTone != error)
        {
            errorTone = error;
            accentBinding.Dispose();
            accentBinding = accent.Bind(BackgroundProperty, this.GetResourceObservable(error ? "PixelError" : "PixelAccent"));
        }
        Opacity = 1;
        IsHitTestVisible = IsEnabled = true;
        ResumeCountdown();
    }

    private void ResumeCountdown()
    {
        if (!persistent && !pointerOver && IsHitTestVisible) { elapsed.Start(); timer.Start(); }
    }
}
