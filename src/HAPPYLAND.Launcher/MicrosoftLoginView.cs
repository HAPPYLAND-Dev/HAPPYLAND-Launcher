using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using System.ComponentModel;
using System.Diagnostics;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;

namespace Happyland.Launcher;

public sealed class MicrosoftLoginView : UserControl
{
    private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Button copy = new() { Content = "复制", Height = 24, MinHeight = 0, Padding = new Thickness(6, 0), IsEnabled = false };
    private readonly SelectableTextBlock code = new() { Text = "正在获取验证码…", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock status = new() { Text = "正在获取微软设备代码…", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button reopen = new() { Content = "重新打开登录窗口", IsEnabled = false };
    private readonly Button external = new() { Content = "在系统浏览器中打开", IsEnabled = false };
    private readonly string root;
    private Process? browser;
    private MicrosoftDeviceCodeInfo? deviceCode;

    public MicrosoftLoginView(string root, Func<string, Task> copyCode, Action<string> openBrowser)
    {
        this.root = root;
        Focusable = true;
        copy.Click += async (_, _) => await copyCode(deviceCode!.UserCode);
        reopen.Click += (_, _) => _ = OpenLoginWindowAsync();
        external.Click += (_, _) => openBrowser(deviceCode!.VerificationUrl);
        CodeHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 0), HorizontalAlignment = HorizontalAlignment.Right, Children =
        {
            new TextBlock { Text = "验证码", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center }, code, copy
        } };
        Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children =
        {
            status, new WrapPanel { Children = { reopen, external } }
        } };
    }

    public Control CodeHeader { get; }

    public void ShowCode(MicrosoftDeviceCodeInfo info)
    {
        deviceCode = info;
        code.Text = info.UserCode;
        copy.IsEnabled = external.IsEnabled = true;
        _ = OpenLoginWindowAsync();
    }

    private async Task OpenLoginWindowAsync()
    {
        reopen.IsEnabled = false;
        status.Text = "在登录窗口输入上方验证码。授权完成后，启动器会自动添加账号。";
        var info = deviceCode!;
        try
        {
            using var process = MicrosoftLoginBrowser.Start(root, info);
            browser = process;
            await process.WaitForExitAsync();
            status.Text = process.ExitCode == 0 ? "登录窗口已关闭，正在等待微软确认授权。" :
                "登录窗口未能加载页面。可以重新打开，或在系统浏览器中继续。";
        }
        catch (Exception e) when (e is Win32Exception or IOException)
        {
            status.Text = $"登录窗口启动失败：{e.Message}";
        }
        finally
        {
            browser = null;
            reopen.IsEnabled = true;
        }
    }

    public Task<bool> Result => completion.Task;
    public void Close()
    {
        browser?.StandardInput.Close();
        completion.TrySetResult(true);
    }
    public void FocusFirstField() => Focus();
}
