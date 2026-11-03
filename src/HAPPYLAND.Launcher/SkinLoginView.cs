using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Happyland.Contracts;

namespace Happyland.Launcher;

public sealed record SkinLogin(SkinServer Server, string Username, string Password);

public sealed class SkinLoginView : UserControl
{
    private readonly TaskCompletionSource<SkinLogin?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TextBox user = new() { Watermark = "邮箱或用户名" };
    private readonly TextBox pass = new() { Watermark = "密码", PasswordChar = '*' };

    public SkinLoginView(IReadOnlyList<SkinServer> skinServers, string? username = null)
    {
        user.Text = username;
        var sites = new ComboBox { ItemsSource = skinServers.Select(s => s.Name).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var website = new Button { Content = "皮肤站官网", Classes = { "quiet" }, HorizontalAlignment = HorizontalAlignment.Right };
        website.Click += async (_, _) => await SiteRequested!(skinServers[sites.SelectedIndex]);
        var submit = new Button { Content = "登录", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Stretch, Classes = { "primary" } };
        var cancel = new Button { Content = "取消", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        cancel.Click += (_, _) => Cancel();
        submit.Click += (_, _) =>
        {
            if (sites.SelectedIndex < 0) { ValidationError?.Invoke("请选择皮肤站。"); sites.Focus(); return; }
            if (string.IsNullOrWhiteSpace(user.Text)) { ValidationError?.Invoke("请输入邮箱或用户名。"); user.Focus(); return; }
            if (string.IsNullOrEmpty(pass.Text)) { ValidationError?.Invoke("请输入密码。"); pass.Focus(); return; }
            completion.TrySetResult(new SkinLogin(skinServers[sites.SelectedIndex], user.Text.Trim(), pass.Text));
        };
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*"), Children = { cancel, submit } };
        Grid.SetColumn(submit, 2);
        var siteHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children =
            { new TextBlock { Text = "皮肤站", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center }, website } };
        Grid.SetColumn(website, 1);
        Content = new StackPanel { Margin = new Thickness(16), Spacing = 8, Children =
        {
            siteHeader, sites,
            new TextBlock { Text = "账号", Classes = { "muted" } }, user,
            new TextBlock { Text = "密码", Classes = { "muted" } }, pass,
            buttons
        } };
    }

    public Task<SkinLogin?> Result => completion.Task;
    public event Action<string>? ValidationError;
    public event Func<SkinServer, Task>? SiteRequested;

    public void Cancel() => completion.TrySetResult(null);

    public void FocusFirstField()
    {
        if (string.IsNullOrWhiteSpace(user.Text)) user.Focus(); else pass.Focus();
    }
}
