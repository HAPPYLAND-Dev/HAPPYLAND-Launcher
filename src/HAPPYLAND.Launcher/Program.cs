using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Markup.Xaml.Styling;
using Happyland.Launcher.Appearance;

namespace Happyland.Launcher;

internal static class Program
{
    public static IReadOnlyDictionary<string, string?> GameDisplayEnvironment { get; private set; } = new Dictionary<string, string?>();

    [STAThread]
    public static int Main(string[] args)
    {
        // Avalonia's GTK initialization changes these for the launcher process.
        if (OperatingSystem.IsLinux())
            GameDisplayEnvironment = new Dictionary<string, string?>
            {
                ["WAYLAND_DISPLAY"] = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
                ["GDK_BACKEND"] = Environment.GetEnvironmentVariable("GDK_BACKEND")
            };
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect()
        .With(new Win32PlatformOptions { OverlayPopups = true })
        .With(new X11PlatformOptions { OverlayPopups = true })
        .With(new AvaloniaNativePlatformOptions { OverlayPopups = true });
}

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://HAPPYLAND.Launcher/"))
        {
            Source = new Uri("avares://HAPPYLAND.Launcher/Appearance/PixelStyles.axaml")
        });
        PixelTheme.Apply(this, "雾蓝");
        PixelTypography.Apply(this, "标准");
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
