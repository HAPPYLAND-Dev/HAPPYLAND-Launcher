using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Happyland.Launcher.Appearance;

public enum PixelIconKind { Play, Plus, Down, Gear, Close, Minimize, Check, File, Maximize, Restore, Palette, Folder, Bell, Book, Skin, Link }

public sealed class PixelIcon : Control
{
    public static readonly StyledProperty<PixelIconKind> KindProperty =
        AvaloniaProperty.Register<PixelIcon, PixelIconKind>(nameof(Kind));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextBlock.ForegroundProperty.AddOwner<PixelIcon>();

    private static readonly string[][] Patterns =
    [
        ["1000", "1100", "1110", "1111", "1110", "1100", "1000"],
        ["00100", "00100", "11111", "00100", "00100"],
        ["10001", "01010", "00100"],
        ["0011100", "0111110", "1110111", "1100011", "1110111", "0111110", "0011100"],
        ["10001", "01010", "00100", "01010", "10001"],
        ["11111"],
        ["000001", "000010", "100100", "011000"],
        ["1111100", "1000110", "1000011", "1000001", "1000001", "1000001", "1111111"],
        ["1111111", "1000001", "1000001", "1000001", "1000001", "1000001", "1111111"],
        ["0011111", "0010001", "1111101", "1000101", "1000111", "1000100", "1111100"],
        ["0011100", "0100010", "1010101", "1000001", "1010011", "0100100", "0011100"],
        ["1110000", "1001111", "1111111", "1000001", "1000001", "1000001", "1111111"],
        ["0001000", "0011100", "0100010", "0100010", "0100010", "1111111", "0001000"],
        ["1110111", "1010101", "1010101", "1010101", "1010101", "1111111", "0001000"],
        ["1100011", "1110111", "0111110", "0100010", "0100010", "0100010", "0111110"],
        ["0011100", "0101010", "1001001", "1111111", "1001001", "0101010", "0011100"]
    ];

    static PixelIcon() => AffectsRender<PixelIcon>(KindProperty, ForegroundProperty);

    public PixelIcon()
    {
        Width = Height = 16;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    public PixelIconKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public override void Render(DrawingContext context)
    {
        var pattern = Patterns[(int)Kind];
        var brush = Foreground ?? (this.TryFindResource("PixelText", out var resource) ? (IBrush)resource! : Brushes.White);
        var unit = Math.Min(Bounds.Width, Bounds.Height) / 8;
        var left = (Bounds.Width - pattern[0].Length * unit) / 2;
        var top = (Bounds.Height - pattern.Length * unit) / 2;
        for (var y = 0; y < pattern.Length; y++)
            for (var x = 0; x < pattern[y].Length; x++)
                if (pattern[y][x] == '1') context.FillRectangle(brush, new Rect(left + x * unit, top + y * unit, unit, unit));
    }
}
