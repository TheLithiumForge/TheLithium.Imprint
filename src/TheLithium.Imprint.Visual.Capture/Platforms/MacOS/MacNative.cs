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

    // Keep one framework reference for the lifetime of its exported CFString constants.
    private static class WindowKeys
    {
        private static readonly nint Library = NativeLibrary.Load(CoreGraphics);
        internal static readonly nint Owner = Key("kCGWindowOwnerPID");
        internal static readonly nint Number = Key("kCGWindowNumber");
        internal static readonly nint Layer = Key("kCGWindowLayer");
        internal static readonly nint Bounds = Key("kCGWindowBounds");
        internal static readonly nint Name = Key("kCGWindowName");

        private static nint Key(string symbol) => *(nint*)NativeLibrary.GetExport(Library, symbol);
    }

    /// <summary>Normal-layer windows on screen for the given processes, with their bounds in points.</summary>
    internal static List<MacWindow> Windows(IReadOnlySet<int> processes)
    {
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
                var owner = (int)Number(window, WindowKeys.Owner);
                if (!processes.Contains(owner) || Number(window, WindowKeys.Layer) != 0)
                {
                    continue;
                }
                var number = Number(window, WindowKeys.Number);
                if (number is > 0 and <= uint.MaxValue
                    && CGRectMakeWithDictionaryRepresentation(CFDictionaryGetValue(window, WindowKeys.Bounds), out var rect)
                    && rect.Width > 1 && rect.Height > 1)
                {
                    windows.Add(new(owner, (uint)number, Text(CFDictionaryGetValue(window, WindowKeys.Name)), rect));
                }
            }
        }
        finally
        {
            CFRelease(list);
        }
        return windows;
    }

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
