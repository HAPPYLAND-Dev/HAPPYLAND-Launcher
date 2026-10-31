using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Markup.Xaml.Styling;
using Happyland.Launcher.Appearance;

namespace Happyland.Launcher;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
