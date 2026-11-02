using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TheLithium.Imprint.Visual.Fixture;

/// <summary>An AppKit window built through the Objective-C runtime, so the fixture needs no macOS workload.</summary>
[SupportedOSPlatform("macos")]
internal static partial class AppKitWindow
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal double X;
        internal double Y;
        internal double Width;
        internal double Height;
    }

    internal static int Run(string title, (byte R, byte G, byte B) right)
    {
        // Loading AppKit registers its classes with the runtime.
        NativeLibrary.Load(AppKit);
        Send(Send(Class("NSAutoreleasePool"), "alloc"), "init");
        var application = Send(Class("NSApplication"), "sharedApplication");
        SendLong(application, Selector("setActivationPolicy:"), 0);

        // Titled, closable and miniaturizable, with a buffered backing store.
        var frame = new Rect { X = 100, Y = 100, Width = Program.Width, Height = Program.Height };
        var window = SendInit(Send(Class("NSWindow"), "alloc"), Selector("initWithContentRect:styleMask:backing:defer:"), frame, 7, 2, false);
        SendObject(window, Selector("setTitle:"), Text(title));
        SendObject(window, Selector("setBackgroundColor:"), Color(Program.Left));

        var half = new Rect { X = Program.Width / 2.0, Y = 0, Width = Program.Width / 2.0, Height = Program.Height };
        var view = SendFrame(Send(Class("NSView"), "alloc"), Selector("initWithFrame:"), half);
        SendBool(view, Selector("setWantsLayer:"), true);
        SendObject(Send(view, "layer"), Selector("setBackgroundColor:"), CGColor(right));
        SendObject(Send(window, "contentView"), Selector("addSubview:"), view);

        SendObject(window, Selector("makeKeyAndOrderFront:"), 0);
        SendBool(application, Selector("activateIgnoringOtherApps:"), true);
        Send(application, "run");
        return 0;
    }

    private static nint Color((byte R, byte G, byte B) color)
        => SendColor(Class("NSColor"), Selector("colorWithSRGBRed:green:blue:alpha:"), color.R / 255.0, color.G / 255.0, color.B / 255.0, 1);

    private static nint CGColor((byte R, byte G, byte B) color) => Send(Color(color), "CGColor");

    private static nint Text(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text + "\0");
        unsafe
        {
            fixed (byte* pointer = bytes)
            {
                return SendObject(Class("NSString"), Selector("stringWithUTF8String:"), (nint)pointer);
            }
        }
    }

    private static nint Class(string name) => objc_getClass(name);

    private static nint Selector(string name) => sel_registerName(name);

    private static nint Send(nint receiver, string selector) => SendPlain(receiver, Selector(selector));

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendPlain(nint receiver, nint selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendObject(nint receiver, nint selector, nint argument);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendLong(nint receiver, nint selector, long argument);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendBool(nint receiver, nint selector, [MarshalAs(UnmanagedType.U1)] bool argument);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendInit(nint receiver, nint selector, Rect frame, ulong style, ulong backing, [MarshalAs(UnmanagedType.U1)] bool defer);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendFrame(nint receiver, nint selector, Rect frame);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendColor(nint receiver, nint selector, double red, double green, double blue, double alpha);
}
