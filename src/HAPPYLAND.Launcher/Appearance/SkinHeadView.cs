using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Happyland.Launcher.Appearance;

public sealed class SkinHeadView : Control
{
    public static readonly StyledProperty<Bitmap?> SkinProperty =
        AvaloniaProperty.Register<SkinHeadView, Bitmap?>(nameof(Skin));

    private static readonly Lazy<Bitmap> DefaultSkin = new(() =>
    {
        using var stream = AssetLoader.Open(new Uri("avares://HAPPYLAND.Launcher/Assets/default-steve.png"));
        return new Bitmap(stream);
    });

    static SkinHeadView() => AffectsRender<SkinHeadView>(SkinProperty);

    public SkinHeadView()
    {
        Width = 18;
        Height = 18;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    // The account owner disposes supplied bitmaps after detaching them from the view.
    public Bitmap? Skin
    {
        get => GetValue(SkinProperty);
        set => SetValue(SkinProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var skin = Skin ?? DefaultSkin.Value;
        var unit = skin.Size.Width / 64;
        var side = Math.Floor(Math.Min(Bounds.Width, Bounds.Height) / 8) * 8;
        var target = new Rect(Math.Floor((Bounds.Width - side) / 2), Math.Floor((Bounds.Height - side) / 2), side, side);
        context.DrawImage(skin, new Rect(8 * unit, 8 * unit, 8 * unit, 8 * unit), target);
        context.DrawImage(skin, new Rect(40 * unit, 8 * unit, 8 * unit, 8 * unit), target);
    }
}
