using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace TheLithium.Imprint.Visual.Platforms.Linux;

/// <summary>The libX11 calls the X11 driver makes, for the 64-bit Linux layout where long and XID are 64 bits.</summary>
[SupportedOSPlatform("linux")]
internal static unsafe partial class X11Native
{
    private const string Library = "libX11.so.6";
    internal const int IsViewable = 2;
    internal const int ZPixmap = 2;
    private static int _initialized;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowAttributes
    {
        internal int X;
        internal int Y;
        internal int Width;
        internal int Height;
        internal int BorderWidth;
        internal int Depth;
        internal nint Visual;
        internal nuint Root;
        internal int Class;
        internal int BitGravity;
        internal int WinGravity;
        internal int BackingStore;
        internal nuint BackingPlanes;
        internal nuint BackingPixel;
        internal int SaveUnder;
        internal nuint Colormap;
        internal int MapInstalled;
        internal int MapState;
        internal nint AllEventMasks;
        internal nint YourEventMask;
        internal nint DoNotPropagateMask;
        internal int OverrideRedirect;
        internal nint Screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Image
    {
        internal int Width;
        internal int Height;
        internal int XOffset;
        internal int Format;
        internal byte* Data;
        internal int ByteOrder;
        internal int BitmapUnit;
        internal int BitmapBitOrder;
        internal int BitmapPad;
        internal int Depth;
        internal int BytesPerLine;
        internal int BitsPerPixel;
        internal nuint RedMask;
        internal nuint GreenMask;
        internal nuint BlueMask;
        internal nint ObjectData;
        internal nint CreateImage;
        internal delegate* unmanaged<Image*, int> DestroyImage;
    }

    [LibraryImport(Library)]
    private static partial int XInitThreads();

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint XOpenDisplay(string? name);

    [LibraryImport(Library)]
    private static partial void XSetAuthorization(byte* name, int nameLength, byte* data, int dataLength);

    private static readonly Lock Authorization = new();

    /// <summary>
    /// Opens a connection with this cookie. Xlib keeps the authorization for the whole process, so setting it and
    /// connecting happen under one lock, and it's cleared again afterwards.
    /// </summary>
    internal static nint Open(string name, ReadOnlySpan<byte> cookie)
    {
        ReadOnlySpan<byte> protocol = "MIT-MAGIC-COOKIE-1"u8;
        lock (Authorization)
        {
            fixed (byte* method = protocol)
            fixed (byte* data = cookie)
            {
                XSetAuthorization(method, protocol.Length, data, cookie.Length);
                try
                {
                    return XOpenDisplay(name);
                }
                finally
                {
                    XSetAuthorization(null, 0, null, 0);
                }
            }
        }
    }

    [LibraryImport(Library)]
    internal static partial int XCloseDisplay(nint display);

    [LibraryImport(Library)]
    internal static partial nuint XDefaultRootWindow(nint display);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nuint XInternAtom(nint display, string name, int onlyIfExists);

    [LibraryImport(Library)]
    internal static partial int XGetWindowProperty(nint display, nuint window, nuint property, nint offset, nint length, int delete, nuint type,
        out nuint actualType, out int actualFormat, out nuint items, out nuint bytesAfter, out nint data);

    [LibraryImport(Library)]
    internal static partial int XFree(nint data);

    [LibraryImport(Library)]
    internal static partial int XQueryTree(nint display, nuint window, out nuint root, out nuint parent, out nint children, out uint count);

    [LibraryImport(Library)]
    internal static partial int XGetWindowAttributes(nint display, nuint window, out WindowAttributes attributes);

    [LibraryImport(Library)]
    internal static partial int XRaiseWindow(nint display, nuint window);

    [LibraryImport(Library)]
    internal static partial int XSync(nint display, int discard);

    [LibraryImport(Library)]
    internal static partial Image* XGetImage(nint display, nuint drawable, int x, int y, uint width, uint height, nuint planes, int format);

    [LibraryImport(Library)]
    private static partial nint XSetErrorHandler(delegate* unmanaged<nint, nint, int> handler);

    /// <summary>Makes Xlib thread safe and stops an X error from ending the test process. Runs once, before any other call.</summary>
    internal static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            XInitThreads();
            XSetErrorHandler(&IgnoreError);
        }
    }

    [UnmanagedCallersOnly]
    private static int IgnoreError(nint display, nint error) => 0;

    /// <summary>A 32-bit-format property as values. Xlib returns those as C longs, which are 64 bits here.</summary>
    internal static nuint[] Cardinals(nint display, nuint window, nuint property)
    {
        if (XGetWindowProperty(display, window, property, 0, 1024, 0, 0, out _, out var format, out var items, out _, out var data) != 0 || data == 0)
        {
            return [];
        }

        try
        {
            if (format != 32)
            {
                return [];
            }
            return new ReadOnlySpan<nuint>((void*)data, (int)items).ToArray();
        }
        finally
        {
            XFree(data);
        }
    }

    internal static string Text(nint display, nuint window, nuint property)
    {
        if (XGetWindowProperty(display, window, property, 0, 1024, 0, 0, out _, out var format, out var items, out _, out var data) != 0 || data == 0)
        {
            return "";
        }

        try
        {
            return format == 8 ? Encoding.UTF8.GetString((byte*)data, (int)items) : "";
        }
        finally
        {
            XFree(data);
        }
    }

    internal static nuint Parent(nint display, nuint window)
    {
        if (XQueryTree(display, window, out _, out var parent, out var children, out _) == 0)
        {
            return 0;
        }
        if (children != 0)
        {
            XFree(children);
        }
        return parent;
    }

    /// <summary>Converts an XImage to RGBA from its own channel masks, so it doesn't assume a byte layout.</summary>
    internal static RgbaImage Convert(Image* image)
    {
        if (image->BitsPerPixel is not (24 or 32))
        {
            throw new SnapshotCaptureException($"The X server returned {image->BitsPerPixel} bits per pixel. The capture reads 24 and 32.");
        }

        var width = image->Width;
        var height = image->Height;
        var bytes = image->BitsPerPixel / 8;
        var (redShift, redBits) = Mask(image->RedMask);
        var (greenShift, greenBits) = Mask(image->GreenMask);
        var (blueShift, blueBits) = Mask(image->BlueMask);
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            var row = image->Data + (long)y * image->BytesPerLine;
            for (var x = 0; x < width; x++)
            {
                var pixel = row + x * bytes;
                ulong value = 0;
                for (var i = 0; i < bytes; i++)
                {
                    var shift = image->ByteOrder == 0 ? i * 8 : (bytes - 1 - i) * 8;
                    value |= (ulong)pixel[i] << shift;
                }
                pixels[y * width + x] = Rgba32.Opaque(Channel(value, redShift, redBits), Channel(value, greenShift, greenBits), Channel(value, blueShift, blueBits));
            }
        }
        return RgbaImage.FromPixels(width, height, pixels);
    }

    private static (int Shift, int Bits) Mask(nuint mask)
    {
        var value = (ulong)mask;
        if (value == 0)
        {
            return (0, 8);
        }
        var shift = System.Numerics.BitOperations.TrailingZeroCount(value);
        return (shift, System.Numerics.BitOperations.PopCount(value));
    }

    private static byte Channel(ulong value, int shift, int bits)
    {
        var raw = (int)((value >> shift) & ((1UL << bits) - 1));
        return bits == 8 ? (byte)raw : (byte)(raw * 255 / ((1 << bits) - 1));
    }
}
