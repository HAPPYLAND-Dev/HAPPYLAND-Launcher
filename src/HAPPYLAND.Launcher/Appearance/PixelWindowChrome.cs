using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Happyland.Launcher.Appearance;

public sealed class PixelWindowChrome : Decorator
{
    public static readonly StyledProperty<double> PixelUnitProperty =
        AvaloniaProperty.Register<PixelWindowChrome, double>(nameof(PixelUnit), 1);
    public static readonly StyledProperty<IBrush> BackgroundProperty =
        AvaloniaProperty.Register<PixelWindowChrome, IBrush>(nameof(Background), Brushes.Transparent);
    public static readonly StyledProperty<IBrush> BorderBrushProperty =
        AvaloniaProperty.Register<PixelWindowChrome, IBrush>(nameof(BorderBrush), Brushes.Transparent);

    private StreamGeometry? outline;
    private StreamGeometry? border;

    static PixelWindowChrome()
    {
        AffectsMeasure<PixelWindowChrome>(PixelUnitProperty);
        AffectsRender<PixelWindowChrome>(PixelUnitProperty, BackgroundProperty, BorderBrushProperty);
    }

    public PixelWindowChrome() => RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

    public double PixelUnit { get => GetValue(PixelUnitProperty); set => SetValue(PixelUnitProperty, value); }
    public IBrush Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public IBrush BorderBrush { get => GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = new Thickness(PixelUnit);
        Child?.Measure(availableSize.Deflate(padding));
        return (Child?.DesiredSize ?? default).Inflate(padding);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize).Deflate(new Thickness(PixelUnit)));
        return finalSize;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty || change.Property == PixelUnitProperty || change.Property == ChildProperty)
            UpdateShape();
    }

    private void UpdateShape()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            outline = border = null;
            Clip = null;
            return;
        }
        var bounds = new Rect(Bounds.Size);
        var innerBounds = bounds.Deflate(new Thickness(PixelUnit));
        outline = new StreamGeometry();
        using (var context = outline.Open()) WriteContour(context, bounds, 3);
        border = new StreamGeometry();
        using (var context = border.Open())
        {
            context.SetFillRule(FillRule.EvenOdd);
            WriteContour(context, bounds, 3);
            WriteContour(context, innerBounds, 2);
        }
        Clip = outline;
        if (Child is not null)
        {
            var childClip = new StreamGeometry();
            using (var context = childClip.Open()) WriteContour(context, new Rect(innerBounds.Size), 2);
            Child.Clip = childClip;
        }
    }

    private void WriteContour(StreamGeometryContext context, Rect rect, int steps)
    {
        var u = PixelUnit;
        context.BeginFigure(new Point(rect.Left + steps * u, rect.Top), true);
        context.LineTo(new Point(rect.Right - steps * u, rect.Top));
        for (var step = steps; step > 0; step--)
        {
            context.LineTo(new Point(rect.Right - step * u, rect.Top + (steps - step + 1) * u));
            context.LineTo(new Point(rect.Right - (step - 1) * u, rect.Top + (steps - step + 1) * u));
        }
        context.LineTo(new Point(rect.Right, rect.Bottom - steps * u));
        for (var step = steps; step > 0; step--)
        {
            context.LineTo(new Point(rect.Right - (steps - step + 1) * u, rect.Bottom - step * u));
            context.LineTo(new Point(rect.Right - (steps - step + 1) * u, rect.Bottom - (step - 1) * u));
        }
        context.LineTo(new Point(rect.Left + steps * u, rect.Bottom));
        for (var step = steps; step > 0; step--)
        {
            context.LineTo(new Point(rect.Left + step * u, rect.Bottom - (steps - step + 1) * u));
            context.LineTo(new Point(rect.Left + (step - 1) * u, rect.Bottom - (steps - step + 1) * u));
        }
        context.LineTo(new Point(rect.Left, rect.Top + steps * u));
        for (var step = steps; step > 0; step--)
        {
            context.LineTo(new Point(rect.Left + (steps - step + 1) * u, rect.Top + step * u));
            context.LineTo(new Point(rect.Left + (steps - step + 1) * u, rect.Top + (step - 1) * u));
        }
        context.EndFigure(true);
    }

    public override void Render(DrawingContext context)
    {
        if (outline is not null) context.DrawGeometry(Background, null, outline);
        if (border is not null) context.DrawGeometry(BorderBrush, null, border);
    }
}
