using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Happyland.Launcher.Appearance;

public sealed record BackgroundPreset(string Id, string Name, string FileName, int Strength, int Shade, AlignmentY Alignment = AlignmentY.Center)
{
    public static IReadOnlyList<BackgroundPreset> All { get; } =
    [
        new("forest-path", "林间小径", "forest-path.jpg", 65, 25),
        new("forest-garden", "秘境花园", "forest-garden.jpg", 60, 30),
        new("greenhouse", "阳光温室", "greenhouse.jpg", 55, 35),
        new("alpine-lake", "高山湖畔", "alpine-lake.jpg", 60, 35, AlignmentY.Bottom)
    ];

    public Bitmap Load(int width = 1600)
    {
        using var input = AssetLoader.Open(new Uri("avares://HAPPYLAND.Launcher/Assets/Backgrounds/" + FileName));
        return Bitmap.DecodeToWidth(input, width, BitmapInterpolationMode.HighQuality);
    }

    public override string ToString() => Name;
}
