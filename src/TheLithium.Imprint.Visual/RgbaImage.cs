using System.Runtime.InteropServices;
using TheLithium.Imprint.Configuration;
using TheLithium.Imprint.Generation;
using TheLithium.Imprint.Visual.Png;

namespace TheLithium.Imprint.Visual;

/// <summary>
/// An image as straight 8-bit RGBA pixels, top row first. It's immutable, so a capture can't change after it's taken.
/// </summary>
[SnapshotCaptureType("AssertImage")]
public sealed class RgbaImage
{
    private readonly Rgba32[] _pixels;

    internal RgbaImage(int width, int height, Rgba32[] pixels)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    /// <summary>The width in physical pixels.</summary>
    public int Width
    {
        get;
    }

    /// <summary>The height in physical pixels.</summary>
    public int Height
    {
        get;
    }

    /// <summary>Every pixel, row by row from the top.</summary>
    public ReadOnlySpan<Rgba32> Pixels => _pixels;

    /// <summary>The pixel at column <paramref name="x"/> of row <paramref name="y"/>.</summary>
    public Rgba32 this[int x, int y]
    {
        get
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            {
                throw new ArgumentOutOfRangeException(nameof(x), $"{x},{y} is outside the {Width}x{Height} image.");
            }
            return _pixels[y * Width + x];
        }
    }

    /// <summary>Reads a PNG. It accepts every non-interlaced color type and bit depth, and bounds memory before decoding.</summary>
    /// <param name="png">The PNG file content.</param>
    /// <param name="maxPixels">The largest image to accept. Defaults to the visual.maxPixelsPerImage default.</param>
    public static RgbaImage FromPng(ReadOnlySpan<byte> png, int maxPixels = SnapshotLimits.DefaultImagePixels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPixels, 1);
        return PngDecoder.Decode(png, maxPixels);
    }

    /// <summary>Copies pixels into a new image.</summary>
    public static RgbaImage FromPixels(int width, int height, ReadOnlySpan<Rgba32> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if ((long)width * height != pixels.Length)
        {
            throw new ArgumentException($"A {width}x{height} image needs {(long)width * height} pixels, not {pixels.Length}.", nameof(pixels));
        }
        return new(width, height, pixels.ToArray());
    }

    /// <summary>
    /// Converts 32-bit BGRA or BGRX rows, as Windows and X11 return them, to straight RGBA.
    /// <paramref name="opaque"/> sets alpha to 255 for buffers whose fourth byte is unused.
    /// </summary>
    internal static RgbaImage FromBgra(int width, int height, ReadOnlySpan<byte> rows, int stride, bool opaque)
    {
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            var source = rows.Slice(y * stride, width * 4);
            var target = pixels.AsSpan(y * width, width);
            for (var x = 0; x < width; x++)
            {
                var pixel = source.Slice(x * 4, 4);
                target[x] = new(pixel[2], pixel[1], pixel[0], opaque ? (byte)255 : pixel[3]);
            }
        }
        return new(width, height, pixels);
    }

    /// <summary>Writes the image as a PNG with fixed settings, so the same pixels give the same file.</summary>
    public byte[] ToPng() => PngEncoder.Encode(this);

    /// <summary>A new image of one area of this one.</summary>
    public RgbaImage Crop(PixelRect area)
    {
        if (!area.FitsIn(Width, Height))
        {
            throw new ArgumentOutOfRangeException(nameof(area), $"{area} doesn't fit inside the {Width}x{Height} image.");
        }

        var pixels = new Rgba32[area.Width * area.Height];
        for (var y = 0; y < area.Height; y++)
        {
            _pixels.AsSpan((area.Y + y) * Width + area.X, area.Width).CopyTo(pixels.AsSpan(y * area.Width, area.Width));
        }
        return new(area.Width, area.Height, pixels);
    }

    /// <summary>True when both images have the same size and every pixel is the same.</summary>
    public bool PixelsEqual(RgbaImage other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Width == other.Width && Height == other.Height
            && MemoryMarshal.AsBytes(Pixels).SequenceEqual(MemoryMarshal.AsBytes(other.Pixels));
    }
}
