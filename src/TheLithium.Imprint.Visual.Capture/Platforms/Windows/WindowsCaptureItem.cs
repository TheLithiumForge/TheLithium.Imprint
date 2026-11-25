using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TheLithium.Imprint.Visual.Platforms.Windows;

/// <summary>A Windows.Graphics.Capture session bound to a single HWND.</summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WindowsCaptureItem : IDisposable
{
    private const int BufferCount = 2;
    private const int Bgra8 = 87;
    private const string CaptureNamespace = "Windows.Graphics.Capture.";

    private static readonly Guid ItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid CaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid SessionStatics = new("2224A540-5974-49AA-B232-0882536F4CB5");
    private static readonly Guid PoolStatics2 = new("589B103F-6BBC-5DF5-A991-02E28B3B66D5");
    private static readonly Guid Session2 = new("2C39AE40-7D2E-5044-804E-8B6799D4CF9E");
    private static readonly Guid Closable = new("30D5A829-7FA4-4026-83BB-D75BAE4EA99E");
    private static readonly Guid DxgiDevice = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
    private static readonly Guid Direct3DDevice = new("A37624AB-8D5F-4650-9D3E-9EAE3D9BC670");
    private static readonly Guid DxgiAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private static readonly Guid Texture2D = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    private nint _pool;
    private nint _session;
    internal int PoolWidth { get; }
    internal int PoolHeight { get; }

    private WindowsCaptureItem(Size size)
    {
        PoolWidth = size.Width;
        PoolHeight = size.Height;
    }

    internal static bool IsSupported()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return false;
        }

        using var factory = Factory("GraphicsCaptureSession", SessionStatics);
        byte supported;
        var isSupported = (delegate* unmanaged[Stdcall]<nint, byte*, int>)Methods(factory.Pointer)[6];
        Check(isSupported(factory.Pointer, &supported), "check Windows Graphics Capture support");
        return supported != 0;
    }

    internal static WindowsCaptureItem Open(nint window, nint d3dDevice, int maxPixels)
    {
        using var factory = Factory("GraphicsCaptureItem", ItemInterop);
        var createForWindow = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)Methods(factory.Pointer)[3];
        var iid = CaptureItem;
        nint captureItem;
        Check(createForWindow(factory.Pointer, window, &iid, &captureItem), "select the window for capture");
        using var item = new Interface(captureItem);

        Size size;
        var getSize = (delegate* unmanaged[Stdcall]<nint, Size*, int>)Methods(captureItem)[7];
        Check(getSize(captureItem, &size), "read the window's capture size");
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new SnapshotCaptureException("The window has no drawable capture area.");
        }
        if ((long)size.Width * size.Height > maxPixels)
        {
            throw new SnapshotCaptureException($"The window is {size.Width}x{size.Height}, more than the {maxPixels}-pixel limit. Raise visual.maxPixelsPerImage if it's expected.");
        }

        var capture = new WindowsCaptureItem(size);
        try
        {
            capture._pool = CreatePool(d3dDevice, size);
            var createSession = (delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Methods(capture._pool)[10];
            nint session;
            Check(createSession(capture._pool, captureItem, &session), "create the window capture session");
            capture._session = session;
            using var settings = Query(session, Session2);
            var setCursorCapture = (delegate* unmanaged[Stdcall]<nint, byte, int>)Methods(settings.Pointer)[7];
            Check(setCursorCapture(settings.Pointer, 0), "turn off cursor capture");
            var startCapture = (delegate* unmanaged[Stdcall]<nint, int>)Methods(session)[6];
            Check(startCapture(session), "start window capture");
            return capture;
        }
        catch
        {
            capture.Dispose();
            throw;
        }
    }

    private static nint CreatePool(nint d3dDevice, Size size)
    {
        using var dxgi = Query(d3dDevice, DxgiDevice);
        Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi.Pointer, out var wrapper), "wrap the Direct3D device for capture");
        using var inspectable = new Interface(wrapper);
        using var device = Query(wrapper, Direct3DDevice);
        using var factory = Factory("Direct3D11CaptureFramePool", PoolStatics2);
        var createFreeThreaded = (delegate* unmanaged[Stdcall]<nint, nint, int, int, Size, nint*, int>)Methods(factory.Pointer)[6];
        nint pool;
        Check(createFreeThreaded(factory.Pointer, device.Pointer, Bgra8, BufferCount, size, &pool), "create the window capture frame pool");
        return pool;
    }

    /// <summary>Drains the queued frames and returns only the newest texture for GPU readback.</summary>
    internal Frame? TakeLatestFrame()
    {
        var latest = TryGetFrame();
        if (latest is null)
        {
            return null;
        }
        try
        {
            for (var i = 1; i < BufferCount; i++)
            {
                var next = TryGetFrame();
                if (next is null)
                {
                    break;
                }
                latest.Dispose();
                latest = next;
            }
            return latest;
        }
        catch
        {
            latest.Dispose();
            throw;
        }
    }

    private Frame? TryGetFrame()
    {
        nint frame;
        var tryGetNextFrame = (delegate* unmanaged[Stdcall]<nint, nint*, int>)Methods(_pool)[7];
        Check(tryGetNextFrame(_pool, &frame), "read the next window capture frame");
        if (frame == 0)
        {
            return null;
        }

        try
        {
            Size size;
            var getContentSize = (delegate* unmanaged[Stdcall]<nint, Size*, int>)Methods(frame)[8];
            Check(getContentSize(frame, &size), "read the frame's content size");
            var getSurface = (delegate* unmanaged[Stdcall]<nint, nint*, int>)Methods(frame)[6];
            nint surface;
            Check(getSurface(frame, &surface), "read the captured surface");
            using var surfaceReference = new Interface(surface);
            using var access = Query(surface, DxgiAccess);
            var getInterface = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Methods(access.Pointer)[3];
            var iid = Texture2D;
            nint texture;
            Check(getInterface(access.Pointer, &iid, &texture), "read the captured texture");
            return new Frame(frame, texture, size.Width, size.Height);
        }
        catch
        {
            Close(ref frame);
            throw;
        }
    }

    internal sealed class Frame(nint frame, nint texture, int width, int height) : IDisposable
    {
        private nint _frame = frame;
        private nint _texture = texture;
        internal nint Texture => _texture;
        internal int Width { get; } = width;
        internal int Height { get; } = height;

        public void Dispose()
        {
            if (_texture != 0)
            {
                Marshal.Release(_texture);
                _texture = 0;
            }
            Close(ref _frame);
        }
    }

    public void Dispose()
    {
        Close(ref _session);
        Close(ref _pool);
    }

    // Layouts follow windows.graphics.capture.h and windows.graphics.capture.interop.h in the Windows SDK.
    // IUnknown starts with 3 methods, IInspectable with 6. Named delegates above identify each additional slot.
    private static nint* Methods(nint instance) => *(nint**)instance;

    private static Interface Query(nint pointer, Guid iid)
    {
        Check(Marshal.QueryInterface(pointer, in iid, out var result), "get a Windows capture interface");
        return new(result);
    }

    private static Interface Factory(string name, Guid iid)
    {
        var runtimeClass = CaptureNamespace + name;
        Check(WindowsCreateString(runtimeClass, runtimeClass.Length, out var classId), "create a Windows Runtime class name");
        try
        {
            Check(RoGetActivationFactory(classId, in iid, out var pointer), $"open {runtimeClass}");
            return new(pointer);
        }
        finally
        {
            WindowsDeleteString(classId);
        }
    }

    private static void Close(ref nint pointer)
    {
        if (pointer == 0)
        {
            return;
        }
        if (Marshal.QueryInterface(pointer, in Closable, out var closable) >= 0)
        {
            var close = (delegate* unmanaged[Stdcall]<nint, int>)Methods(closable)[6];
            close(closable);
            Marshal.Release(closable);
        }
        Marshal.Release(pointer);
        pointer = 0;
    }

    private static void Check(int status, string operation)
    {
        if (status < 0)
        {
            throw new SnapshotCaptureException($"Windows couldn't {operation} (HRESULT 0x{status:X8}).");
        }
    }

    private readonly struct Interface(nint pointer) : IDisposable
    {
        internal nint Pointer { get; } = pointer;
        public void Dispose() => Marshal.Release(Pointer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        internal int Width;
        internal int Height;
    }

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string value, int length, out nint result);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint value);

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(nint classId, in Guid iid, out nint factory);

    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
}
