using System.Runtime.InteropServices;

namespace Happyland.Launcher;

internal static class LinuxWebViewFrameSync
{
    private const string Gtk = "libgtk-3.so.0";
    private const string Gdk = "libgdk-3.so.0";
    private const string Glib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SourceCallback(IntPtr widget);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DestroyCallback(IntPtr widget);
    private static readonly SourceCallback Apply = ApplyOnGtkThread;
    private static readonly DestroyCallback Release = g_object_unref;

    public static void Disable(IntPtr widget)
    {
        // GTK's reparented window cannot receive _NET_WM_FRAME_DRAWN from the window manager.
        g_object_ref(widget);
        if (g_idle_add_full(0, Apply, widget, Release) == 0) g_object_unref(widget);
    }

    private static int ApplyOnGtkThread(IntPtr widget)
    {
        var topLevel = gtk_widget_get_toplevel(widget);
        var window = gtk_widget_get_window(topLevel);
        if (window != IntPtr.Zero)
        {
            gdk_x11_window_set_frame_sync_enabled(window, 0);
            gtk_widget_queue_draw(widget);
        }
        return 0;
    }

    [DllImport(GObject, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr g_object_ref(IntPtr instance);
    [DllImport(GObject, CallingConvention = CallingConvention.Cdecl)] private static extern void g_object_unref(IntPtr instance);
    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] private static extern uint g_idle_add_full(int priority, SourceCallback function, IntPtr data, DestroyCallback notify);
    [DllImport(Gtk, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr gtk_widget_get_toplevel(IntPtr widget);
    [DllImport(Gtk, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr gtk_widget_get_window(IntPtr widget);
    [DllImport(Gtk, CallingConvention = CallingConvention.Cdecl)] private static extern void gtk_widget_queue_draw(IntPtr widget);
    [DllImport(Gdk, CallingConvention = CallingConvention.Cdecl)] private static extern void gdk_x11_window_set_frame_sync_enabled(IntPtr window, int enabled);
}
