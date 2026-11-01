using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Happyland.Contracts;

namespace Happyland.Launcher;

public sealed class AnnouncementView : UserControl
{
    private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ComboBox? history;

    public AnnouncementView(IReadOnlyList<LauncherAnnouncement> announcements)
    {
        MaxHeight = 240;
        var ordered = announcements.OrderByDescending(item => item.PublishedAt).ToArray();
        if (ordered.Length == 0)
        {
            Focusable = true;
            Content = new Border { MinHeight = 80, Padding = new Thickness(16), Child = new TextBlock
            {
                Text = "暂无更新公告", HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" }
            } };
            return;
        }

        history = new ComboBox { ItemsSource = ordered, HorizontalAlignment = HorizontalAlignment.Stretch };
        history.ItemTemplate = new FuncDataTemplate<LauncherAnnouncement>((announcement, _) =>
        {
            if (announcement is null) return null;
            var name = new TextBlock { Text = announcement.Title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var published = new TextBlock { Text = FormatDate(announcement.PublishedAt), VerticalAlignment = VerticalAlignment.Center, Classes = { "small", "muted" } };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto"), Children = { name, published } };
            Grid.SetColumn(published, 2);
            ToolTip.SetTip(row, announcement.Title);
            return row;
        });
        Avalonia.Automation.AutomationProperties.SetName(history, "历史公告");

        var title = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "title" } };
        var date = new TextBlock { Classes = { "small", "muted" } };
        var text = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };
        text.Bind(SelectableTextBlock.FontFamilyProperty, this.GetResourceObservable("LauncherFontFamily"));
        text.Bind(SelectableTextBlock.FontSizeProperty, this.GetResourceObservable("LauncherFontSize"));
        text.Bind(SelectableTextBlock.LineHeightProperty, this.GetResourceObservable("LauncherFontLineHeight"));
        text.Bind(SelectableTextBlock.ForegroundProperty, this.GetResourceObservable("PixelText"));
        text.Bind(SelectableTextBlock.SelectionBrushProperty, this.GetResourceObservable("PixelAccent"));
        text.Bind(SelectableTextBlock.SelectionForegroundBrushProperty, this.GetResourceObservable("PixelAccentText"));
        var body = new ScrollViewer
        {
            Content = text,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        var layout = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,8,Auto,4,Auto,12,*"), Children = { history, title, date, body } };
        Grid.SetRow(title, 2); Grid.SetRow(date, 4); Grid.SetRow(body, 6);
        Content = layout;

        void ShowAnnouncement()
        {
            var selected = (LauncherAnnouncement)history.SelectedItem!;
            title.Text = selected.Title;
            ToolTip.SetTip(title, selected.Title);
            date.Text = selected.PublishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            text.Text = selected.Content;
            body.Offset = default;
        }
        history.SelectionChanged += (_, _) => ShowAnnouncement();
        history.SelectedIndex = 0;
    }

    public Task<bool> Result => completion.Task;

    public void Cancel() => completion.TrySetResult(false);

    public void FocusFirstField()
    {
        if (history is null) Focus(); else history.Focus();
    }

    private static string FormatDate(DateTimeOffset value) => value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
