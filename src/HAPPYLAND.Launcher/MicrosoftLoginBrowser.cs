using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;

namespace Happyland.Launcher;

internal static class MicrosoftLoginBrowser
{
    internal const string Argument = "--microsoft-login-window";

    public static Process Start(string root, MicrosoftDeviceCodeInfo info)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        start.ArgumentList.Add(Argument);
        start.ArgumentList.Add(root);
        var process = Process.Start(start)!;
        try
        {
            process.StandardInput.WriteLine(JsonSerializer.Serialize(info));
            return process;
        }
        catch
        {
            process.StandardInput.Close();
            process.Dispose();
            throw;
        }
    }

    public static int Run(string root)
    {
        if (Console.ReadLine() is not { } request) return 1;
        var info = JsonSerializer.Deserialize<MicrosoftDeviceCodeInfo>(request)!;
        Program.BuildAvaloniaApp().SetupWithoutStarting();
        using var lifetime = new CancellationTokenSource();
        // EOF means the launcher finished, cancelled sign-in, or exited.
        _ = Task.Run(() => { Console.ReadLine(); Dispatcher.UIThread.Post(lifetime.Cancel); });

        var source = new Uri(info.VerificationUrl);
        var title = $"HAPPYLAND 微软登录　验证码 {info.UserCode}";
        var exitCode = 0;
        void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess) { exitCode = 1; lifetime.Cancel(); }
        }
        void OpenPopup(WebViewNewWindowRequestedEventArgs e, Action<Uri> navigate)
        {
            e.Handled = true;
            if (e.Request is { Scheme: "https" } uri) navigate(uri);
        }

        if (OperatingSystem.IsLinux() && !WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WebKitGtk).IsInstalled)
        {
            // NativeWebDialog on Linux requires GTK. WPE renders into an ordinary Avalonia window.
            if (!WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WpeWebKit).IsInstalled) return 1;
            var web = new NativeWebView { Source = source };
            web.EnvironmentRequested += (_, e) =>
            {
                if (e is LinuxWpeWebViewEnvironmentRequestedEventArgs wpe)
                {
                    wpe.DataDirectory = Path.Combine(root, "webview", "data");
                    wpe.CacheDirectory = Path.Combine(root, "webview", "cache");
                }
            };
            web.NewWindowRequested += (_, e) => OpenPopup(e, web.Navigate);
            web.NavigationCompleted += OnNavigationCompleted;
            var window = new Window { Title = title, Width = 760, Height = 600, Content = web };
            window.Closed += (_, _) => lifetime.Cancel();
            window.Show();
            Dispatcher.UIThread.MainLoop(lifetime.Token);
        }
        else
        {
            if (OperatingSystem.IsWindows() && !WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WebView2).IsInstalled) return 1;
            using var dialog = new NativeWebDialog { Title = title, Source = source, CanUserResize = true };
            dialog.Resize(760, 600);
            dialog.EnvironmentRequested += (_, e) =>
            {
                if (e is WindowsWebView2EnvironmentRequestedEventArgs windows)
                {
                    windows.IsInPrivateModeEnabled = true;
                    windows.UserDataFolder = Path.Combine(root, "webview");
                }
                else if (e is GtkWebViewEnvironmentRequestedEventArgs gtk) gtk.EphemeralDataManager = true;
                else if (e is AppleWKWebViewEnvironmentRequestedEventArgs apple) apple.NonPersistentDataStore = true;
            };
            dialog.NewWindowRequested += (_, e) => OpenPopup(e, dialog.Navigate);
            dialog.NavigationCompleted += OnNavigationCompleted;
            dialog.Closing += (_, _) => lifetime.Cancel();
            dialog.Show();
            Dispatcher.UIThread.MainLoop(lifetime.Token);
        }
        return exitCode;
    }
}
