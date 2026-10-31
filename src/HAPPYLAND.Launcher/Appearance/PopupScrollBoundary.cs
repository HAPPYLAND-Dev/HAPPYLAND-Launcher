using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Happyland.Launcher.Appearance;

public sealed class PopupScrollBoundary : AvaloniaObject
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<PopupScrollBoundary, Popup, bool>("IsEnabled");

    static PopupScrollBoundary()
    {
        IsEnabledProperty.Changed.AddClassHandler<Popup>((popup, change) =>
        {
            if (change.GetNewValue<bool>())
                popup.AddHandler(Control.RequestBringIntoViewEvent, StopAtPopup, RoutingStrategies.Bubble);
            else
                popup.RemoveHandler(Control.RequestBringIntoViewEvent, StopAtPopup);
        });
    }

    public static bool GetIsEnabled(Popup popup) => popup.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(Popup popup, bool value) => popup.SetValue(IsEnabledProperty, value);

    private static void StopAtPopup(object? sender, RequestBringIntoViewEventArgs e)
    {
        // The popup's own scroll presenter has already had a chance to scroll its content.
        e.Handled = true;
    }
}
