using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TheLithium.Imprint.Visual.Platforms.Windows;

/// <summary>Copies a window's D3D11 capture texture into a CPU-readable texture on its capture thread.</summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WindowsCaptureDevice : IDisposable
{
    private const int Hardware = 1;
    private const int Warp = 5;
    private const uint BgraSupport = 0x20;
    private const uint SdkVersion = 7;
    private const uint Bgra8 = 87;
    private const uint Staging = 3;
    private const uint CpuRead = 0x20000;
    private const uint MapRead = 1;
    private const uint DoNotWait = 0x100000;
    private const int StillDrawing = unchecked((int)0x887A000A);

    private nint _device;
    private nint _context;
    private nint _staging;
    private int _width;
    private int _height;

    private WindowsCaptureDevice(nint device, nint context) => (_device, _context) = (device, context);

    internal nint Device => _device;

    internal static WindowsCaptureDevice Create()
    {
        var result = D3D11CreateDevice(0, Hardware, 0, BgraSupport, null, 0, SdkVersion, out var device, null, out var context);
        if (result < 0)
        {
            // WARP supplies a software device on machines without a hardware adapter, including CI runners.
            result = D3D11CreateDevice(0, Warp, 0, BgraSupport, null, 0, SdkVersion, out device, null, out context);
        }
        Check(result, "create a D3D11 capture device");
        return new(device, context);
    }

    internal RgbaImage Read(nint texture, int width, int height, int maxPixels, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (width <= 0 || height <= 0 || (long)width * height > maxPixels)
        {
            throw new SnapshotCaptureException($"The captured frame is {width}x{height}. The pixel limit is {maxPixels}.");
        }

        // ID3D11Texture2D::GetDesc. The pool can still have its old surface size during a window resize.
        var getDesc = (delegate* unmanaged[Stdcall]<nint, TextureDescription*, void>)Vtable(texture)[10];
        TextureDescription source;
        getDesc(texture, &source);
        if (source.Format != Bgra8 || source.SampleCount != 1 || source.Width < width || source.Height < height)
        {
            throw new SnapshotCaptureException("The Windows capture texture has an unexpected size or pixel format.");
        }

        EnsureStagingTexture(width, height);
        // ID3D11DeviceContext::CopySubresourceRegion. Padding outside ContentSize is undefined.
        var region = new TextureBox { Right = (uint)width, Bottom = (uint)height, Back = 1 };
        var copy = (delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, uint, nint, uint, TextureBox*, void>)Vtable(_context)[46];
        copy(_context, _staging, 0, 0, 0, 0, texture, 0, &region);
        var flush = (delegate* unmanaged[Stdcall]<nint, void>)Vtable(_context)[111];
        flush(_context);

        // ID3D11DeviceContext::Map/Unmap. A pending GPU copy must leave time to cancel the capture.
        var map = (delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, MappedTexture*, int>)Vtable(_context)[14];
        MappedTexture mapped;
        int result;
        while ((result = map(_context, _staging, 0, MapRead, DoNotWait, &mapped)) == StillDrawing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(1);
        }
        Check(result, "read the captured window texture");
        try
        {
            var pixels = new Rgba32[width * height];
            for (var y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = (byte*)mapped.Data + (nuint)y * mapped.RowPitch;
                var output = pixels.AsSpan(y * width, width);
                for (var x = 0; x < width; x++, row += 4)
                {
                    var alpha = row[3];
                    output[x] = alpha switch
                    {
                        255 => new(row[2], row[1], row[0], 255),
                        0 => default,
                        _ => new(Straight(row[2], alpha), Straight(row[1], alpha), Straight(row[0], alpha), alpha)
                    };
                }
            }
            return new(width, height, pixels);
        }
        finally
        {
            var unmap = (delegate* unmanaged[Stdcall]<nint, nint, uint, void>)Vtable(_context)[15];
            unmap(_context, _staging, 0);
        }
    }

    public void Dispose()
    {
        Release(ref _staging);
        Release(ref _context);
        Release(ref _device);
    }

    private void EnsureStagingTexture(int width, int height)
    {
        if (_staging != 0 && width == _width && height == _height)
        {
            return;
        }
        Release(ref _staging);
        var description = new TextureDescription
        {
            Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1, Format = Bgra8,
            SampleCount = 1, Usage = Staging, CpuAccessFlags = CpuRead
        };
        var createTexture = (delegate* unmanaged[Stdcall]<nint, TextureDescription*, nint, nint*, int>)Vtable(_device)[5];
        nint texture;
        Check(createTexture(_device, &description, 0, &texture), "create a CPU-readable window texture");
        _staging = texture;
        (_width, _height) = (width, height);
    }

    private static byte Straight(byte channel, byte alpha) => (byte)Math.Min(255, (channel * 255 + alpha / 2) / alpha);

    private static nint* Vtable(nint instance) => *(nint**)instance;

    private static void Release(ref nint instance)
    {
        if (instance != 0)
        {
            Marshal.Release(instance);
            instance = 0;
        }
    }

    private static void Check(int result, string operation)
    {
        if (result < 0)
        {
            throw new SnapshotCaptureException($"Windows couldn't {operation} (HRESULT 0x{result:X8}).");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TextureDescription
    {
        internal uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TextureBox
    {
        internal uint Left, Top, Front, Right, Bottom, Back;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MappedTexture
    {
        internal nint Data;
        internal uint RowPitch, DepthPitch;
    }

    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags,
        int* featureLevels, uint featureLevelCount, uint sdkVersion, out nint device, int* featureLevel, out nint immediateContext);
}
