using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Happyland.Launcher.Appearance;

public static class PixelWindowSizing
{
    public static void Attach(Window window, LayoutTransformControl viewport, int logicalWidth, int logicalHeight, Action<double>? unitChanged = null)
    {
        var previousDpi = window.RenderScaling;

        void UpdateTransform()
        {
            var width = Math.Round(window.ClientSize.Width * window.RenderScaling);
            var height = Math.Round(window.ClientSize.Height * window.RenderScaling);
            var pixels = Math.Clamp(Math.Floor(Math.Min(width / logicalWidth, height / logicalHeight)), 1, 3);
            var unit = pixels / window.RenderScaling;
            viewport.LayoutTransform = new ScaleTransform(unit, unit);
            unitChanged?.Invoke(unit);
        }

        void UpdateLimits()
        {
            var dpi = window.RenderScaling;
            var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
            var availableWidth = screen?.WorkingArea.Width ?? logicalWidth * 3;
            var availableHeight = screen?.WorkingArea.Height ?? logicalHeight * 3;
            window.MinWidth = Math.Min(logicalWidth * 2, availableWidth) / dpi;
            window.MinHeight = Math.Min(logicalHeight * 2, availableHeight) / dpi;
            window.MaxWidth = availableWidth / dpi;
            window.MaxHeight = availableHeight / dpi;
        }

        void DefaultSize()
        {
            UpdateLimits();
            window.Width = Math.Min(logicalWidth * 3 / window.RenderScaling, window.MaxWidth);
            window.Height = Math.Min(logicalHeight * 3 / window.RenderScaling, window.MaxHeight);
            UpdateTransform();
        }

        DefaultSize();
        window.Opened += (_, _) => DefaultSize();
        window.SizeChanged += (_, _) => UpdateTransform();
        window.ScalingChanged += (_, _) =>
        {
            var ratio = previousDpi / window.RenderScaling;
            previousDpi = window.RenderScaling;
            var width = window.Width * ratio;
            var height = window.Height * ratio;
            UpdateLimits();
            if (window.WindowState == WindowState.Normal)
            {
                window.Width = Math.Clamp(width, window.MinWidth, window.MaxWidth);
                window.Height = Math.Clamp(height, window.MinHeight, window.MaxHeight);
            }
            UpdateTransform();
        };
    }
}
