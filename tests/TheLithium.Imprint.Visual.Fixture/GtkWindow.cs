using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TheLithium.Imprint.Visual.Fixture;

/// <summary>A GTK 4 window with a drawing area, so the same fixture runs on X11 and on Wayland.</summary>
[SupportedOSPlatform("linux")]
internal static unsafe partial class GtkWindow
{
    private const string Gtk = "libgtk-4.so.1";
    private const string Cairo = "libcairo.so.2";
    private const string GLib = "libglib-2.0.so.0";
    private static (double R, double G, double B) _right;

    internal static int Run(string title, (byte R, byte G, byte B) right)
    {
        _right = (right.R / 255.0, right.G / 255.0, right.B / 255.0);
        gtk_init();
        var window = gtk_window_new();
        gtk_window_set_title(window, title);
        gtk_window_set_default_size(window, Program.Width, Program.Height);
        gtk_window_set_resizable(window, 0);
        var area = gtk_drawing_area_new();
        gtk_drawing_area_set_content_width(area, Program.Width);
        gtk_drawing_area_set_content_height(area, Program.Height);
        gtk_drawing_area_set_draw_func(area, &Draw, 0, 0);
        gtk_window_set_child(window, area);
        gtk_window_present(window);
        while (true)
        {
            g_main_context_iteration(0, 1);
        }
    }

    [UnmanagedCallersOnly]
    private static void Draw(nint area, nint cairo, int width, int height, nint data)
    {
        var middle = width / 2;
        cairo_set_source_rgb(cairo, Program.Left.R / 255.0, Program.Left.G / 255.0, Program.Left.B / 255.0);
        cairo_rectangle(cairo, 0, 0, middle, height);
        cairo_fill(cairo);
        cairo_set_source_rgb(cairo, _right.R, _right.G, _right.B);
        cairo_rectangle(cairo, middle, 0, width - middle, height);
        cairo_fill(cairo);
    }

    [LibraryImport(Gtk)]
    private static partial void gtk_init();

    [LibraryImport(Gtk)]
    private static partial nint gtk_window_new();

    [LibraryImport(Gtk, StringMarshalling = StringMarshalling.Utf8)]
    private static partial void gtk_window_set_title(nint window, string title);

    [LibraryImport(Gtk)]
    private static partial void gtk_window_set_default_size(nint window, int width, int height);

    [LibraryImport(Gtk)]
    private static partial void gtk_window_set_resizable(nint window, int resizable);

    [LibraryImport(Gtk)]
    private static partial nint gtk_drawing_area_new();

    [LibraryImport(Gtk)]
    private static partial void gtk_drawing_area_set_content_width(nint area, int width);

    [LibraryImport(Gtk)]
    private static partial void gtk_drawing_area_set_content_height(nint area, int height);

    [LibraryImport(Gtk)]
    private static partial void gtk_drawing_area_set_draw_func(nint area, delegate* unmanaged<nint, nint, int, int, nint, void> draw, nint data, nint destroy);

    [LibraryImport(Gtk)]
    private static partial void gtk_window_set_child(nint window, nint child);

    [LibraryImport(Gtk)]
    private static partial void gtk_window_present(nint window);

    [LibraryImport(GLib)]
    private static partial int g_main_context_iteration(nint context, int mayBlock);

    [LibraryImport(Cairo)]
    private static partial void cairo_set_source_rgb(nint cairo, double red, double green, double blue);

    [LibraryImport(Cairo)]
    private static partial void cairo_rectangle(nint cairo, double x, double y, double width, double height);

    [LibraryImport(Cairo)]
    private static partial void cairo_fill(nint cairo);
}
