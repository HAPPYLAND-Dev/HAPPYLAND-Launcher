using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Happyland.Launcher.Appearance;

public sealed class LoginMethodBadge : Control
{
    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#263540"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#F35325"));
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#81BC06"));
    private static readonly IBrush Blue = new SolidColorBrush(Color.Parse("#05A6F0"));
    private static readonly IBrush Yellow = new SolidColorBrush(Color.Parse("#FFBA08"));
    private static readonly IBrush SkinBackground = new SolidColorBrush(Color.Parse("#AB5827"));
    private static readonly IBrush SkinForeground = Brushes.White;

    public LoginMethodBadge() { Width = Height = 8; RenderOptions.SetEdgeMode(this, EdgeMode.Aliased); }
    public bool IsMicrosoft { get; init; }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Background, new Rect(0, 0, 8, 8));
        if (IsMicrosoft)
        {
            context.FillRectangle(Red, new Rect(1, 1, 2, 2));
            context.FillRectangle(Green, new Rect(4, 1, 2, 2));
            context.FillRectangle(Blue, new Rect(1, 4, 2, 2));
            context.FillRectangle(Yellow, new Rect(4, 4, 2, 2));
            return;
        }
        context.FillRectangle(SkinBackground, new Rect(1, 1, 6, 6));
        context.FillRectangle(SkinForeground, new Rect(2, 2, 4, 1));
        context.FillRectangle(SkinForeground, new Rect(1, 3, 6, 1));
        context.FillRectangle(SkinForeground, new Rect(2, 4, 4, 2));
    }
}
