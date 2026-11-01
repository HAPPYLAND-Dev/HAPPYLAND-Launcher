using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Happyland.Launcher;

public sealed class ErrorDetailsView : UserControl
{
    private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Button copy = new() { Content = "复制错误", Classes = { "primary" } };

    public ErrorDetailsView(string details, Func<Task> copyDetails)
    {
        Height = 240;
        var text = new SelectableTextBlock { Text = details, TextWrapping = TextWrapping.Wrap };
        text.Bind(SelectableTextBlock.FontFamilyProperty, this.GetResourceObservable("LauncherSmallFamily"));
        text.Bind(SelectableTextBlock.FontSizeProperty, this.GetResourceObservable("LauncherSmallSize"));
        text.Bind(SelectableTextBlock.LineHeightProperty, this.GetResourceObservable("LauncherSmallLineHeight"));
        text.Bind(SelectableTextBlock.ForegroundProperty, this.GetResourceObservable("PixelText"));
        text.Bind(SelectableTextBlock.SelectionBrushProperty, this.GetResourceObservable("PixelAccent"));
        text.Bind(SelectableTextBlock.SelectionForegroundBrushProperty, this.GetResourceObservable("PixelAccentText"));
        copy.Click += async (_, _) => await copyDetails();
        var close = new Button { Content = "关闭" };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { close, copy } };
        var body = new ScrollViewer { Content = text, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var layout = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("*,12,Auto"), Children = { body, buttons } };
        Grid.SetRow(buttons, 2);
        Content = layout;
    }

    public Task<bool> Result => completion.Task;
    public void Close() => completion.TrySetResult(true);
    public void FocusFirstField() => copy.Focus();
}
