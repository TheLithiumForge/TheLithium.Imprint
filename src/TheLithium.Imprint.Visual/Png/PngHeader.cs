using System.Buffers.Binary;

namespace TheLithium.Imprint.Visual.Png;

internal enum PngColorType
{
    Gray = 0,
    Rgb = 2,
    Palette = 3,
    GrayAlpha = 4,
    Rgba = 6
}

/// <summary>A validated IHDR chunk, with the buffer sizes it implies checked before anything is allocated.</summary>
internal sealed class PngHeader
{
    private PngHeader(int width, int height, int bitDepth, PngColorType colorType)
    {
        Width = width;
        Height = height;
        BitDepth = bitDepth;
        ColorType = colorType;
        Channels = colorType switch
        {
            PngColorType.Gray or PngColorType.Palette => 1,
            PngColorType.GrayAlpha => 2,
            PngColorType.Rgb => 3,
            PngColorType.Rgba => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(colorType), colorType, null)
        };
        RowBytes = (int)(((long)width * Channels * bitDepth + 7) / 8);
        RawSize = (int)((long)height * (RowBytes + 1));
        FilterBytesPerPixel = Math.Max(1, Channels * bitDepth / 8);
    }

    internal int Width
    {
        get;
    }
    internal int Height
    {
        get;
    }
    internal int BitDepth
    {
        get;
    }
    internal PngColorType ColorType
    {
        get;
    }
    internal int Channels
    {
        get;
    }
    internal int RowBytes
    {
        get;
    }
    internal int RawSize
    {
        get;
    }
    internal int FilterBytesPerPixel
    {
        get;
    }

    internal static PngHeader Read(ReadOnlySpan<byte> body, int maxPixels)
    {
        if (body.Length != 13)
        {
            throw PngDecoder.Invalid("The PNG's IHDR chunk has an invalid length.");
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(body);
        var height = BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
        int depth = body[8];
        var color = body[9];
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
        {
            throw PngDecoder.Invalid("The PNG has an invalid width or height.");
        }
        if (body[10] != 0 || body[11] != 0)
        {
            throw PngDecoder.Invalid("The PNG uses an unknown compression or filter method.");
        }
        if (body[12] == 1)
        {
            throw PngDecoder.Invalid("Interlaced PNGs aren't supported. Save the image without interlacing.");
        }
        if (body[12] != 0)
        {
            throw PngDecoder.Invalid("The PNG uses an unknown interlace method.");
        }
        if (!ValidDepth((PngColorType)color, depth))
        {
            throw PngDecoder.Invalid($"The PNG combines color type {color} with bit depth {depth}, which PNG doesn't allow.");
        }
        if ((long)width * height > maxPixels)
        {
            throw PngDecoder.Invalid($"The PNG is {width}x{height}, more than the {maxPixels}-pixel limit. Raise visual.maxPixelsPerImage if it's expected.");
        }

        var header = new PngHeader((int)width, (int)height, depth, (PngColorType)color);
        if ((long)header.Height * (header.RowBytes + 1) > Array.MaxLength)
        {
            throw PngDecoder.Invalid("The PNG is too large to decode.");
        }
        return header;
    }

    private static bool ValidDepth(PngColorType color, int depth) => color switch
    {
        PngColorType.Gray => depth is 1 or 2 or 4 or 8 or 16,
        PngColorType.Palette => depth is 1 or 2 or 4 or 8,
        PngColorType.Rgb or PngColorType.GrayAlpha or PngColorType.Rgba => depth is 8 or 16,
        _ => false
    };
}
