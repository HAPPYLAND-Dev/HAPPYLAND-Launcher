using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Happyland.Launcher.Appearance;

public static class PixelTheme
{
    public static IReadOnlyList<string> PresetNames { get; } = ["深海蓝", "雾蓝", "森林绿", "暖砂", "石墨", "月白", "暮紫", "赤陶"];

    private sealed record Palette(bool Dark, string Background, string Header, string Panel, string Text,
        string Muted, string Border, string Accent, string AccentText, string Shadow, string Success, string Error);

    private static readonly Dictionary<string, Palette> Palettes = new()
    {
        ["深海蓝"] = new(true, "#111e27", "#14242f", "#1b2e39", "#edf1e9", "#92a8ae", "#304652", "#bfd0aa", "#17251c", "#71856b", "#a9d6ac", "#f3aaa3"),
        ["雾蓝"] = new(false, "#edf2f2", "#e0e9eb", "#f7f9f8", "#294552", "#647e87", "#adc1c5", "#326a80", "#f7f9f8", "#214d62", "#39756d", "#a63837"),
        ["森林绿"] = new(true, "#182e28", "#203b32", "#29483c", "#e2ebd9", "#a1b8a1", "#507663", "#bbcf87", "#20372c", "#738957", "#a5d1a3", "#ffb29e"),
        ["暖砂"] = new(false, "#f1e9dc", "#e5d8c4", "#faf5ec", "#4c3b33", "#7c675b", "#bca78d", "#915a3e", "#fff5e7", "#5f3e2f", "#4e7752", "#a33138"),
        ["石墨"] = new(true, "#202126", "#25272d", "#2b2f36", "#e7e7e9", "#adb1bd", "#424a58", "#b8bfde", "#292d3d", "#777f9e", "#a7c7b6", "#ffb0b0"),
        ["月白"] = new(false, "#f5f5ef", "#eeeee6", "#fdfdf8", "#2d3935", "#77817a", "#d8ded3", "#617764", "#fcfff7", "#425345", "#557f61", "#a64a45"),
        ["暮紫"] = new(true, "#211f2a", "#292633", "#302b3d", "#eeebf6", "#ada1be", "#4d425e", "#c8b7e2", "#2b2236", "#7c6797", "#b0d3b6", "#efaaa9"),
        ["赤陶"] = new(false, "#f2e8e0", "#eaded4", "#faf2ea", "#4e3c35", "#94796b", "#d8c4b7", "#ad6f55", "#fff8ee", "#80513d", "#5a7d60", "#a03e38")
    };

    public static void Apply(Application app, bool dark, Bitmap? background = null) =>
        Apply(app, dark ? "深海蓝" : "雾蓝", background);

    public static void Apply(Application app, string preset, Bitmap? background = null, string accent = "主题默认", bool hasBackground = false)
    {
        var palette = background is null
            ? Palettes.GetValueOrDefault(preset, Palettes["雾蓝"])
            : BackgroundPalette(background);
        app.RequestedThemeVariant = palette.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var colors = new Dictionary<string, Color>
        {
            ["PixelBackground"] = Color.Parse(palette.Background), ["PixelHeader"] = Color.Parse(palette.Header),
            ["PixelPanel"] = Color.Parse(palette.Panel), ["PixelText"] = Color.Parse(palette.Text),
            ["PixelMuted"] = Color.Parse(palette.Muted), ["PixelBorder"] = Color.Parse(palette.Border),
            ["PixelAccent"] = Color.Parse(palette.Accent), ["PixelAccentText"] = Color.Parse(palette.AccentText),
            ["PixelAccentShadow"] = Color.Parse(palette.Shadow), ["PixelSuccess"] = Color.Parse(palette.Success),
            ["PixelError"] = Color.Parse(palette.Error)
        };
        var chosenAccent = background is null ? accent switch { "薄荷" => "#a4cbb9", "琥珀" => "#dfbd86", "鸢尾" => "#b9b5df", "珊瑚" => "#e0aa99", _ => null } : null;
        if (chosenAccent is not null)
        {
            var color = Color.Parse(chosenAccent);
            colors["PixelAccent"] = palette.Dark ? color : Mix(color, Colors.Black, .38);
            colors["PixelAccentText"] = palette.Dark ? Color.Parse("#17242b") : Colors.White;
            colors["PixelAccentShadow"] = Mix(colors["PixelAccent"], Colors.Black, .28);
        }
        colors["PixelPanelHover"] = Mix(colors["PixelPanel"], colors["PixelText"], .05);
        colors["PixelPanelPressed"] = Mix(colors["PixelPanel"], colors["PixelText"], .13);
        colors["PixelHeaderHover"] = Mix(colors["PixelHeader"], colors["PixelText"], .07);
        colors["PixelHeaderPressed"] = Mix(colors["PixelHeader"], colors["PixelText"], .12);
        colors["PixelBorderHover"] = Mix(colors["PixelBorder"], colors["PixelText"], .22);
        colors["PixelAccentHover"] = Mix(colors["PixelAccent"], palette.Dark ? Colors.White : Colors.Black, .07);
        colors["PixelAccentPressed"] = Mix(colors["PixelAccent"], colors["PixelAccentShadow"], .30);
        if (hasBackground || background is not null)
        {
            foreach (var (key, alpha) in new (string, byte)[]
            {
                ("PixelHeader", 196), ("PixelHeaderHover", 216), ("PixelHeaderPressed", 230),
                ("PixelPanel", 190), ("PixelPanelHover", 212), ("PixelPanelPressed", 228),
                ("PixelAccent", 238), ("PixelAccentHover", 246), ("PixelAccentPressed", 246)
            })
            {
                var color = colors[key];
                colors[key] = Color.FromArgb(alpha, color.R, color.G, color.B);
            }
        }
        // Preserve hover RGB at zero alpha so brush transitions avoid transparent white.
        foreach (var (key, source) in new[] { ("PixelPanelClear", "PixelPanelHover"), ("PixelHeaderClear", "PixelHeaderHover"), ("PixelBorderClear", "PixelBorderHover") })
        {
            var color = colors[source];
            colors[key] = Color.FromArgb(0, color.R, color.G, color.B);
        }
        foreach (var (key, color) in colors) app.Resources[key] = new SolidColorBrush(color);
        void Alias(string resource, string pixelResource) => app.Resources[resource] = app.Resources[pixelResource];
        foreach (var state in new[] { "", "PointerOver", "Focused", "Disabled" })
        {
            Alias("TextControlForeground" + state, state == "Disabled" ? "PixelMuted" : "PixelText");
            Alias("TextControlPlaceholderForeground" + state, "PixelMuted");
            Alias("TextControlBackground" + state, state is "PointerOver" or "Focused" ? "PixelPanelHover" : "PixelPanel");
            Alias("TextControlBorderBrush" + state, state is "PointerOver" or "Focused" ? "PixelBorderHover" : "PixelBorder");
        }
        foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled", "Selected", "SelectedPointerOver", "SelectedPressed", "SelectedDisabled" })
        {
            var surface = state.Contains("Pressed") ? "PixelPanelPressed" : state.Contains("PointerOver") || state == "Selected" ? "PixelPanelHover" : "PixelPanel";
            Alias("ComboBoxItemBackground" + state, surface);
            Alias("ComboBoxItemBorderBrush" + state, "PixelBorder");
            Alias("ComboBoxItemForeground" + state, state.Contains("Disabled") ? "PixelMuted" : "PixelText");
        }
        foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            Alias("MenuFlyoutItemBackground" + state, state == "Pressed" ? "PixelPanelPressed" : state == "PointerOver" ? "PixelPanelHover" : "PixelPanel");
            Alias("MenuFlyoutItemForeground" + state, state == "Disabled" ? "PixelMuted" : "PixelText");
            Alias("MenuFlyoutItemKeyboardAcceleratorTextForeground" + state, "PixelMuted");
            Alias("MenuFlyoutSubItemChevron" + state, "PixelMuted");
            Alias("ScrollBarButtonBackground" + state, state == "Pressed" ? "PixelPanelPressed" : state == "PointerOver" ? "PixelPanelHover" : "PixelHeader");
            Alias("ScrollBarButtonBorderBrush" + state, "PixelBorder");
            Alias("ScrollBarButtonArrowForeground" + state, "PixelMuted");
            foreach (var checkState in new[] { "Unchecked", "Checked", "Indeterminate" })
                Alias("CheckBoxForeground" + checkState + state, state == "Disabled" ? "PixelMuted" : "PixelText");
        }
        Alias("ScrollBarPanningThumbBackground", "PixelBorder");
        Alias("ScrollBarThumbFillPointerOver", "PixelBorderHover");
        Alias("ScrollBarThumbFillPressed", "PixelMuted");
        Alias("ScrollBarThumbFillDisabled", "PixelBorder");
        Alias("ScrollBarTrackFill", "PixelHeader");
        Alias("ScrollBarTrackStroke", "PixelHeader");
        app.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
        app.Resources["ControlCornerRadius"] = new CornerRadius(0);
        app.Resources["OverlayCornerRadius"] = new CornerRadius(0);
        app.Resources["ButtonCornerRadius"] = new CornerRadius(0);
        app.Resources["TextControlCornerRadius"] = new CornerRadius(0);
        app.Resources["ComboBoxCornerRadius"] = new CornerRadius(0);
        app.Resources["FlyoutContentCornerRadius"] = new CornerRadius(0);
    }

    private static Color Mix(Color a, Color b, double amount) => Color.FromRgb(
        (byte)Math.Round(a.R * (1 - amount) + b.R * amount),
        (byte)Math.Round(a.G * (1 - amount) + b.G * amount),
        (byte)Math.Round(a.B * (1 - amount) + b.B * amount));

    private static Palette BackgroundPalette(Bitmap bitmap)
    {
        var dominant = DominantColor(bitmap);
        var dark = Luminance(dominant) < .42;
        var nearBlack = Color.Parse("#111b24");
        var nearWhite = Color.Parse("#f5f7f8");
        var text = dark ? nearWhite : nearBlack;
        var baseColor = dark ? nearBlack : nearWhite;
        var background = Mix(dominant, baseColor, dark ? .82 : .94);
        var header = Mix(dominant, baseColor, dark ? .70 : .85);
        var panel = Mix(dominant, baseColor, dark ? .62 : .96);
        var accent = Mix(dominant, dark ? nearWhite : nearBlack, dark ? .56 : .48);
        var accentLuminance = Luminance(accent);
        var accentText = (accentLuminance + .05) / (Luminance(nearBlack) + .05)
            >= (Luminance(nearWhite) + .05) / (accentLuminance + .05) ? nearBlack : nearWhite;
        return new Palette(dark, background.ToString(), header.ToString(), panel.ToString(), text.ToString(),
            Mix(text, panel, .44).ToString(), Mix(panel, text, .25).ToString(), accent.ToString(),
            accentText.ToString(), Mix(accent, nearBlack, .35).ToString(),
            Mix(dominant, Color.Parse(dark ? "#a9d6ac" : "#39756d"), .90).ToString(),
            Mix(dominant, Color.Parse(dark ? "#f3aaa3" : "#a63837"), .90).ToString());
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            var channel = value / 255d;
            return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }

    private static Color DominantColor(Bitmap bitmap)
    {
        using var sample = bitmap.CreateScaledBitmap(new PixelSize(32, 32), BitmapInterpolationMode.LowQuality);
        using var pixels = new WriteableBitmap(new PixelSize(32, 32), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var buffer = pixels.Lock();
        sample.CopyPixels(buffer, AlphaFormat.Unpremul);
        var bytes = new byte[buffer.RowBytes * 32];
        Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        var bins = new Dictionary<int, (int Count, int R, int G, int B)>();
        for (var y = 0; y < 32; y++)
        for (var x = 0; x < 32; x++)
        {
            var offset = y * buffer.RowBytes + x * 4;
            var b = bytes[offset]; var g = bytes[offset + 1]; var r = bytes[offset + 2];
            if (bytes[offset + 3] < 128) continue;
            var key = (r >> 5) << 6 | (g >> 5) << 3 | b >> 5;
            var bin = bins.GetValueOrDefault(key);
            bins[key] = (bin.Count + 1, bin.R + r, bin.G + g, bin.B + b);
        }
        if (bins.Count == 0) return Color.Parse("#49657b");
        var dominant = bins.Values.MaxBy(b => b.Count);
        return Color.FromRgb((byte)(dominant.R / dominant.Count), (byte)(dominant.G / dominant.Count), (byte)(dominant.B / dominant.Count));
    }
}
