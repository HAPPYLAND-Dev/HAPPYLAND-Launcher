using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;

namespace Happyland.Launcher;

public sealed class MicrosoftLoginView : UserControl
{
    private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TextBlock status = new() { Text = "正在获取验证码…", TextWrapping = TextWrapping.Wrap };
    private readonly SelectableTextBlock code = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly SelectableTextBlock address = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel details = new() { Spacing = 8, IsVisible = false };
    private readonly Button external = new() { Content = "重新打开登录页", IsVisible = false };
    private readonly Action<string> openBrowser;

    public MicrosoftLoginView(Func<string, Task> copyCode, Action<string> openBrowser)
    {
        this.openBrowser = openBrowser;
        Focusable = true;
        code.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("LauncherTitleSize"));
        code.Bind(TextBlock.LineHeightProperty, this.GetResourceObservable("LauncherTitleLineHeight"));
        var copy = new Button { Content = "复制验证码", Classes = { "primary" } };
        copy.Click += async (_, _) => await copyCode(code.Text!);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto"), Children = { code, copy } };
        Grid.SetColumn(copy, 2);
        var codeBox = new Border
        {
            Padding = new Thickness(10, 8), BorderThickness = new Thickness(1),
            Child = new StackPanel { Spacing = 6, Children =
            {
                new TextBlock { Text = "设备验证码", Classes = { "small", "muted" } }, row
            } }
        };
        codeBox.Bind(Border.BackgroundProperty, this.GetResourceObservable("PixelHeader"));
        codeBox.Bind(Border.BorderBrushProperty, this.GetResourceObservable("PixelBorder"));
        details.Children.Add(codeBox);
        details.Children.Add(new StackPanel { Spacing = 4, Children =
        {
            new TextBlock { Text = "微软登录地址", Classes = { "small", "muted" } }, address
        } });

        external.Click += (_, _) => openBrowser(address.Text!);
        var cancel = new Button { Content = "取消登录" };
        cancel.Click += (_, _) => CancelRequested?.Invoke();
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Children = { external, cancel } };
        external.Margin = new Thickness(0, 0, 8, 0);
        Content = new StackPanel { Margin = new Thickness(16, 8, 16, 12), Spacing = 10, Children = { status, details, actions } };
    }

    public event Action? CancelRequested;

    public void ShowCode(MicrosoftDeviceCodeInfo info)
    {
        code.Text = info.UserCode;
        address.Text = info.VerificationUrl;
        status.Text = "在浏览器中输入验证码，完成微软授权。";
        details.IsVisible = external.IsVisible = true;
        openBrowser(info.VerificationUrl);
    }

    public Task<bool> Result => completion.Task;
    public void Close() => completion.TrySetResult(true);
    public void FocusFirstField() => Focus();
}
