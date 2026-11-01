using Avalonia;
using Avalonia.Media;

namespace Happyland.Launcher.Appearance;

public static class PixelTypography
{
    private static readonly FontFamily Pixel8 = Font(8);
    private static readonly FontFamily Pixel10 = Font(10);
    private static readonly FontFamily Pixel12 = Font(12);

    private static FontFamily Font(int pixels) =>
        new($"avares://HAPPYLAND.Launcher/Assets/Fonts#Fusion Pixel {pixels}px Prop zh_hans");

    public static void Apply(Application app, string size)
    {
        var compact = size == "紧凑";
        var large = size == "宽松";
        Set("Font", compact ? Pixel10 : Pixel12, compact ? 10 : large ? 16 : 12, compact ? 16 : large ? 24 : 18);
        Set("Small", compact ? Pixel8 : large ? Pixel12 : Pixel10, compact ? 8 : large ? 12 : 10, compact ? 12 : large ? 18 : 14);
        Set("Title", Pixel12, large ? 24 : 20, large ? 32 : 28);
        Set("Section", Pixel12, large ? 20 : 16, large ? 28 : 24);
        Set("Hero", Pixel12, compact ? 20 : 24, compact ? 28 : 32);
        Set("Stat", Pixel12, compact ? 16 : large ? 24 : 20, compact ? 24 : large ? 32 : 28);

        void Set(string role, FontFamily family, double fontSize, double lineHeight)
        {
            var prefix = "Launcher" + role;
            app.Resources[prefix + "Family"] = family;
            app.Resources[prefix + "Size"] = fontSize;
            app.Resources[prefix + "LineHeight"] = lineHeight;
        }
    }
}
