using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;

namespace Happyland.Launcher;

public sealed class MicrosoftLoginView : UserControl
{
    private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Button copy = new() { Content = "复制", Height = 24, MinHeight = 0, Padding = new Thickness(6, 0), IsEnabled = false };
    private readonly SelectableTextBlock code = new() { Text = "正在获取验证码…", VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid body = new();
    private readonly TextBlock waiting = new() { Text = "正在打开登录页面…", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel unavailable;
    private readonly string root;
    private readonly Func<double> interfaceScale;
    private MicrosoftDeviceCodeInfo? deviceCode;

    public MicrosoftLoginView(string root, Func<double> interfaceScale, Func<string, Task> copyCode, Action<string> openBrowser)
    {
        this.root = root;
        this.interfaceScale = interfaceScale;
        Height = 340;
        Focusable = true;
        copy.Click += async (_, _) => await copyCode(deviceCode!.UserCode);
        CodeHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 0), HorizontalAlignment = HorizontalAlignment.Right, Children =
        {
            new TextBlock { Text = "验证码", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center }, code, copy
        } };

        var browserButton = new Button { Content = "在浏览器中打开", HorizontalAlignment = HorizontalAlignment.Center };
        browserButton.Click += (_, _) => openBrowser(deviceCode!.VerificationUrl);
        unavailable = new StackPanel { IsVisible = false, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = "登录页面无法打开" }, browserButton } };
        body.Children.Add(waiting);
        body.Children.Add(unavailable);
        body.Margin = new Thickness(16, 4, 16, 16);
        Content = body;
    }

    public Control CodeHeader { get; }

    public void ShowCode(MicrosoftDeviceCodeInfo info)
    {
        deviceCode = info;
        code.Text = info.UserCode;
        copy.IsEnabled = true;

        var available = !OperatingSystem.IsLinux() ||
            WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WpeWebKit).IsInstalled ||
            WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WebKitGtk).IsInstalled;
        if (OperatingSystem.IsWindows()) available = WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WebView2).IsInstalled;
        if (!available) { ShowUnavailable(); return; }

        var browser = new ScaledNativeWebView(interfaceScale) { Background = Brushes.White };
        browser.EnvironmentRequested += (_, e) =>
        {
            if (e is WindowsWebView2EnvironmentRequestedEventArgs windows)
            {
                windows.IsInPrivateModeEnabled = true;
                windows.UserDataFolder = Path.Combine(root, "webview");
            }
            else if (e is AppleWKWebViewEnvironmentRequestedEventArgs apple) apple.NonPersistentDataStore = true;
            else if (e is GtkWebViewEnvironmentRequestedEventArgs gtk)
            {
                gtk.EphemeralDataManager = true;
            }
            else if (e is LinuxWpeWebViewEnvironmentRequestedEventArgs wpe)
            {
                if (WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WebKitGtk).IsInstalled) wpe.PreferWebKitGtkInstead = true;
                else
                {
                    wpe.DataDirectory = Path.Combine(root, "webview", "data");
                    wpe.CacheDirectory = Path.Combine(root, "webview", "cache");
                }
            }
        };
        browser.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.Request is { Scheme: "https" } uri) browser.Navigate(uri);
        };
        browser.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess) { browser.IsVisible = false; ShowUnavailable(); }
        };
        browser.PropertyChanged += (_, e) =>
        {
            if (e.Property == NativeWebView.AdapterInfoProperty && browser.AdapterInfo is DetailedWebViewAdapterInfo { IsInstalled: false })
            { browser.IsVisible = false; ShowUnavailable(); }
        };
        browser.AdapterCreated += (_, e) =>
        {
            browser.ArrangePlatformChild(browser.Bounds.Size);
            if (OperatingSystem.IsLinux() && e.TryGetPlatformHandle() is IGtkWebViewPlatformHandle gtk)
                LinuxWebViewFrameSync.Disable(gtk.WebKitWebView);
        };
        body.Children.Insert(0, browser);
        waiting.IsVisible = false;
        browser.Source = new Uri(info.VerificationUrl);
    }

    private void ShowUnavailable() { waiting.IsVisible = false; unavailable.IsVisible = true; }
    public Task<bool> Result => completion.Task;
    public void Close() => completion.TrySetResult(true);
    public void FocusFirstField() => Focus();

    private sealed class ScaledNativeWebView(Func<double> interfaceScale) : NativeWebView
    {
        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = base.ArrangeOverride(finalSize);
            ArrangePlatformChild(size);
            return size;
        }

        public void ArrangePlatformChild(Size size)
        {
            var scale = AdapterInfo?.Type is WebViewAdapterType.WebView2 or WebViewAdapterType.WkWebView or WebViewAdapterType.WebKitGtk
                ? interfaceScale() : 1;
            var hostSize = new Size(size.Width * scale, size.Height * scale);
            foreach (var child in this.GetVisualChildren().OfType<Control>())
            {
                child.Measure(hostSize);
                child.Arrange(new Rect(hostSize));
            }
        }
    }
}
