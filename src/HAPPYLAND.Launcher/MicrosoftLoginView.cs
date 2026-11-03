using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;

namespace Happyland.Launcher;

public sealed class MicrosoftLoginView : UserControl
{
    private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Button copy = new() { Content = "复制", Height = 24, MinHeight = 0, Padding = new Thickness(6, 0), IsEnabled = false };
    private readonly SelectableTextBlock code = new() { Text = "正在获取验证码…", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock status = new() { Text = "正在获取微软设备代码…", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly SelectableTextBlock address = new();
    private readonly Button external = new() { Content = "重新打开登录页", HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = false };
    private readonly Action<string> openBrowser;

    public MicrosoftLoginView(Func<string, Task> copyCode, Action<string> openBrowser)
    {
        this.openBrowser = openBrowser;
        Focusable = true;
        copy.Click += async (_, _) => await copyCode(code.Text!);
        external.Click += (_, _) => openBrowser(address.Text!);
        CodeHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 0), HorizontalAlignment = HorizontalAlignment.Right, Children =
        {
            new TextBlock { Text = "验证码", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center }, code, copy
        } };
        Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children =
        {
            status, address, external
        } };
    }

    public Control CodeHeader { get; }

    public void ShowCode(MicrosoftDeviceCodeInfo info)
    {
        code.Text = info.UserCode;
        address.Text = info.VerificationUrl;
        status.Text = "请在浏览器中输入上方验证码。授权完成后，启动器会自动添加账号。";
        copy.IsEnabled = external.IsEnabled = true;
        openBrowser(info.VerificationUrl);
    }

    public Task<bool> Result => completion.Task;
    public void Close() => completion.TrySetResult(true);
    public void FocusFirstField() => Focus();
}
