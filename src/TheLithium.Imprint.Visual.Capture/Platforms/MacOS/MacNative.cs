using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace TheLithium.Imprint.Visual.Platforms.MacOS;

/// <summary>The CoreGraphics and CoreFoundation calls the macOS driver makes: the permission check and the window list.</summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class MacNative
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint OnScreenOnly = 1;
    private const uint ExcludeDesktopElements = 16;
    private const nint SInt64 = 4;
    private const uint Utf8 = 0x08000100;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal double X;
        internal double Y;
        internal double Width;
        internal double Height;
    }

    /// <summary>True when this process may capture other apps' windows. It never shows a prompt.</summary>
    [LibraryImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool CGPreflightScreenCaptureAccess();

    [LibraryImport(CoreGraphics)]
    private static partial nint CGWindowListCopyWindowInfo(uint options, uint relativeTo);

    [LibraryImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CGRectMakeWithDictionaryRepresentation(nint dictionary, out Rect rect);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFArrayGetCount(nint array);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFArrayGetValueAtIndex(nint array, nint index);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDictionaryGetValue(nint dictionary, nint key);

    [LibraryImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFNumberGetValue(nint number, nint type, out long value);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringGetLength(nint text);

    [LibraryImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFStringGetCString(nint text, byte* buffer, nint size, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint item);

    /// <summary>Every normal-layer window on screen, with its owner, number, title and bounds in points.</summary>
    internal static List<MacWindow> Windows()
    {
        var library = NativeLibrary.Load(CoreGraphics);
        var owner = Key(library, "kCGWindowOwnerPID");
        var number = Key(library, "kCGWindowNumber");
        var layer = Key(library, "kCGWindowLayer");
        var bounds = Key(library, "kCGWindowBounds");
        var name = Key(library, "kCGWindowName");
        var windows = new List<MacWindow>();
        var list = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktopElements, 0);
        if (list == 0)
        {
            return windows;
        }

        try
        {
            for (nint i = 0, count = CFArrayGetCount(list); i < count; i++)
            {
                var window = CFArrayGetValueAtIndex(list, i);
                if (Number(window, layer) != 0 || !CGRectMakeWithDictionaryRepresentation(CFDictionaryGetValue(window, bounds), out var rect))
                {
                    continue;
                }
                windows.Add(new((int)Number(window, owner), (uint)Number(window, number), Text(CFDictionaryGetValue(window, name)), rect));
            }
        }
        finally
        {
            CFRelease(list);
        }
        return windows;
    }

    /// <summary>A CFString constant such as kCGWindowOwnerPID, read from the framework's exported variable.</summary>
    private static nint Key(nint library, string symbol) => *(nint*)NativeLibrary.GetExport(library, symbol);

    private static long Number(nint dictionary, nint key)
    {
        var value = CFDictionaryGetValue(dictionary, key);
        return value != 0 && CFNumberGetValue(value, SInt64, out var number) ? number : -1;
    }

    private static string Text(nint text)
    {
        if (text == 0)
        {
            return "";
        }

        var capacity = (int)CFStringGetLength(text) * 4 + 1;
        var buffer = new byte[capacity];
        fixed (byte* bytes = buffer)
        {
            if (!CFStringGetCString(text, bytes, capacity, Utf8))
            {
                return "";
            }
        }
        return Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0));
    }
}

[SupportedOSPlatform("macos")]
internal sealed record MacWindow(int ProcessId, uint Number, string Title, MacNative.Rect Bounds)
{
    internal string Describe() => $"\"{Title}\", process {ProcessId}, {Bounds.Width:0}x{Bounds.Height:0} points at {Bounds.X:0},{Bounds.Y:0}";
}
