using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace TheLithium.Imprint.Visual.Png;

/// <summary>
/// Reads a PNG into straight RGBA8. It accepts every non-interlaced color type and bit depth the PNG specification
/// allows, checks every chunk length and CRC, and bounds memory from the header before it inflates anything.
/// Color chunks such as iCCP, gAMA and sRGB are ignored, so the stored samples are the decoded values.
/// </summary>
internal static class PngDecoder
{
    internal static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    internal static RgbaImage Decode(ReadOnlySpan<byte> png, int maxPixels)
    {
        if (!png.StartsWith(Signature))
        {
            throw Invalid("The data isn't a PNG.");
        }

        var position = Signature.Length;
        var first = PngChunk.Next(png, ref position);
        if (first.Name != "IHDR")
        {
            throw Invalid("The PNG doesn't start with an IHDR chunk.");
        }

        var header = PngHeader.Read(first.Body, maxPixels);
        byte[] palette = [];
        byte[]? transparency = null;
        using var compressed = new MemoryStream();
        var data = PngData.None;
        while (true)
        {
            var chunk = PngChunk.Next(png, ref position);
            if (chunk.Name != "IDAT" && data == PngData.Reading)
            {
                data = PngData.Done;
            }

            switch (chunk.Name)
            {
                case "IHDR":
                    throw Invalid("The PNG has more than one IHDR chunk.");
                case "PLTE":
                    if (palette.Length != 0 || data != PngData.None)
                    {
                        throw Invalid("The PNG's PLTE chunk is repeated or comes after its image data.");
                    }
                    palette = ReadPalette(chunk.Body, header);
                    break;
                case "tRNS":
                    if (transparency is not null || data != PngData.None)
                    {
                        throw Invalid("The PNG's tRNS chunk is repeated or comes after its image data.");
                    }
                    transparency = chunk.Body.ToArray();
                    break;
                case "IDAT":
                    if (data == PngData.Done)
                    {
                        throw Invalid("The PNG's IDAT chunks aren't consecutive.");
                    }
                    data = PngData.Reading;
                    compressed.Write(chunk.Body);
                    break;
                case "IEND":
                    return Finish(header, palette, transparency, compressed, data);
                default:
                    if (chunk.IsCritical)
                    {
                        throw Invalid($"The PNG has a critical chunk this reader doesn't know: {chunk.Name}.");
                    }
                    break;
            }
        }
    }

    private static RgbaImage Finish(PngHeader header, byte[] palette, byte[]? transparency, MemoryStream compressed, PngData data)
    {
        if (data == PngData.None)
        {
            throw Invalid("The PNG has no IDAT chunk.");
        }
        if (header.ColorType == PngColorType.Palette && palette.Length == 0)
        {
            throw Invalid("A palette PNG needs a PLTE chunk.");
        }

        ValidateTransparency(header, palette, transparency);
        compressed.Position = 0;
        var raw = Inflate(compressed, header.RawSize);
        Unfilter(raw, header);
        return new(header.Width, header.Height, Convert(raw, header, palette, transparency));
    }

    private static byte[] ReadPalette(ReadOnlySpan<byte> body, PngHeader header)
    {
        if (header.ColorType is PngColorType.Gray or PngColorType.GrayAlpha)
        {
            throw Invalid("A grayscale PNG can't have a PLTE chunk.");
        }
        if (body.Length == 0 || body.Length % 3 != 0 || body.Length / 3 > 256)
        {
            throw Invalid("The PNG's PLTE chunk has an invalid length.");
        }
        if (header.ColorType == PngColorType.Palette && body.Length / 3 > 1 << header.BitDepth)
        {
            throw Invalid("The PNG's palette has more entries than its bit depth allows.");
        }
        return body.ToArray();
    }

    private static void ValidateTransparency(PngHeader header, byte[] palette, byte[]? transparency)
    {
        if (transparency is null)
        {
            return;
        }

        var valid = header.ColorType switch
        {
            PngColorType.Gray => transparency.Length == 2,
            PngColorType.Rgb => transparency.Length == 6,
            PngColorType.Palette => transparency.Length <= palette.Length / 3,
            PngColorType.GrayAlpha or PngColorType.Rgba => false,
            _ => throw new ArgumentOutOfRangeException(nameof(header), header.ColorType, null)
        };
        if (!valid)
        {
            throw Invalid("The PNG's tRNS chunk doesn't fit its color type.");
        }
    }

    private static byte[] Inflate(Stream compressed, int size)
    {
        var raw = new byte[size];
        try
        {
            using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
            var filled = 0;
            while (filled < size)
            {
                var read = inflater.Read(raw, filled, size - filled);
                if (read == 0)
                {
                    throw Invalid("The PNG's image data is shorter than its header says.");
                }
                filled += read;
            }

            // A small file must not expand past what its header declared.
            Span<byte> extra = stackalloc byte[1];
            if (inflater.Read(extra) != 0)
            {
                throw Invalid("The PNG's image data is longer than its header says.");
            }
        }
        catch (InvalidDataException error)
        {
            throw Invalid($"The PNG's image data isn't valid zlib: {error.Message}");
        }
        return raw;
    }

    private static void Unfilter(byte[] raw, PngHeader header)
    {
        var stride = header.RowBytes + 1;
        var bpp = header.FilterBytesPerPixel;
        var zero = new byte[header.RowBytes];
        for (var y = 0; y < header.Height; y++)
        {
            var filter = raw[y * stride];
            var row = raw.AsSpan(y * stride + 1, header.RowBytes);
            ReadOnlySpan<byte> prior = y == 0 ? zero : raw.AsSpan((y - 1) * stride + 1, header.RowBytes);
            switch (filter)
            {
                case 0:
                    break;
                case 1:
                    for (var i = bpp; i < row.Length; i++)
                    {
                        row[i] += row[i - bpp];
                    }
                    break;
                case 2:
                    for (var i = 0; i < row.Length; i++)
                    {
                        row[i] += prior[i];
                    }
                    break;
                case 3:
                    for (var i = 0; i < row.Length; i++)
                    {
                        var left = i >= bpp ? row[i - bpp] : 0;
                        row[i] += (byte)((left + prior[i]) >> 1);
                    }
                    break;
                case 4:
                    for (var i = 0; i < row.Length; i++)
                    {
                        var left = i >= bpp ? row[i - bpp] : (byte)0;
                        var upLeft = i >= bpp ? prior[i - bpp] : (byte)0;
                        row[i] += Paeth(left, prior[i], upLeft);
                    }
                    break;
                default:
                    throw Invalid($"The PNG uses an unknown filter type {filter}.");
            }
        }
    }

    internal static byte Paeth(byte left, byte up, byte upLeft)
    {
        var estimate = left + up - upLeft;
        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toUpLeft = Math.Abs(estimate - upLeft);
        if (toLeft <= toUp && toLeft <= toUpLeft)
        {
            return left;
        }
        return toUp <= toUpLeft ? up : upLeft;
    }

    private static Rgba32[] Convert(byte[] raw, PngHeader header, byte[] palette, byte[]? transparency)
    {
        var pixels = new Rgba32[header.Width * header.Height];
        var stride = header.RowBytes + 1;
        var depth = header.BitDepth;
        for (var y = 0; y < header.Height; y++)
        {
            var row = raw.AsSpan(y * stride + 1, header.RowBytes);
            var output = pixels.AsSpan(y * header.Width, header.Width);
            for (var x = 0; x < header.Width; x++)
            {
                output[x] = header.ColorType switch
                {
                    PngColorType.Gray => Gray(Sample(row, x, 0, 1, depth), depth, transparency),
                    PngColorType.GrayAlpha => GrayAlpha(Sample(row, x, 0, 2, depth), Sample(row, x, 1, 2, depth), depth),
                    PngColorType.Rgb => Rgb(row, x, depth, transparency),
                    PngColorType.Palette => Indexed(Sample(row, x, 0, 1, depth), palette, transparency),
                    PngColorType.Rgba => new(Eight(Sample(row, x, 0, 4, depth), depth), Eight(Sample(row, x, 1, 4, depth), depth),
                        Eight(Sample(row, x, 2, 4, depth), depth), Eight(Sample(row, x, 3, 4, depth), depth)),
                    _ => throw new ArgumentOutOfRangeException(nameof(header), header.ColorType, null)
                };
            }
        }
        return pixels;
    }

    /// <summary>The raw value of one sample, at any bit depth, most significant bits first.</summary>
    private static int Sample(ReadOnlySpan<byte> row, int x, int channel, int channels, int depth)
    {
        var index = (long)x * channels + channel;
        switch (depth)
        {
            case 8:
                return row[(int)index];
            case 16:
                return BinaryPrimitives.ReadUInt16BigEndian(row[(int)(index * 2)..]);
            default:
                var bit = index * depth;
                var shift = 8 - depth - (int)(bit & 7);
                return (row[(int)(bit >> 3)] >> shift) & ((1 << depth) - 1);
        }
    }

    /// <summary>Scales a sample to 8 bits. Depths 1, 2 and 4 scale exactly, and 16 rounds to the nearest value.</summary>
    private static byte Eight(int sample, int depth) => depth switch
    {
        8 => (byte)sample,
        16 => (byte)((sample * 255 + 32767) / 65535),
        _ => (byte)(sample * 255 / ((1 << depth) - 1))
    };

    private static Rgba32 Gray(int sample, int depth, byte[]? transparency)
    {
        var value = Eight(sample, depth);
        var transparent = transparency is not null && BinaryPrimitives.ReadUInt16BigEndian(transparency) == sample;
        return new(value, value, value, transparent ? (byte)0 : (byte)255);
    }

    private static Rgba32 GrayAlpha(int gray, int alpha, int depth)
    {
        var value = Eight(gray, depth);
        return new(value, value, value, Eight(alpha, depth));
    }

    private static Rgba32 Rgb(ReadOnlySpan<byte> row, int x, int depth, byte[]? transparency)
    {
        var red = Sample(row, x, 0, 3, depth);
        var green = Sample(row, x, 1, 3, depth);
        var blue = Sample(row, x, 2, 3, depth);
        var transparent = transparency is not null
            && BinaryPrimitives.ReadUInt16BigEndian(transparency) == red
            && BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2)) == green
            && BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4)) == blue;
        return new(Eight(red, depth), Eight(green, depth), Eight(blue, depth), transparent ? (byte)0 : (byte)255);
    }

    private static Rgba32 Indexed(int index, byte[] palette, byte[]? transparency)
    {
        if (index * 3 >= palette.Length)
        {
            throw Invalid($"The PNG uses palette entry {index}, which its PLTE chunk doesn't define.");
        }
        var alpha = transparency is not null && index < transparency.Length ? transparency[index] : (byte)255;
        return new(palette[index * 3], palette[index * 3 + 1], palette[index * 3 + 2], alpha);
    }

    internal static SnapshotCaptureException Invalid(string message) => new(message);

    private enum PngData
    {
        None, Reading, Done
    }

    /// <summary>One length-checked, CRC-checked chunk.</summary>
    private readonly ref struct PngChunk
    {
        private PngChunk(ReadOnlySpan<byte> type, ReadOnlySpan<byte> body)
        {
            Name = Encoding.ASCII.GetString(type);
            IsCritical = (type[0] & 0x20) == 0;
            Body = body;
        }

        internal string Name
        {
            get;
        }
        internal bool IsCritical
        {
            get;
        }
        internal ReadOnlySpan<byte> Body
        {
            get;
        }

        internal static PngChunk Next(ReadOnlySpan<byte> png, ref int position)
        {
            if (png.Length - position < 12)
            {
                throw Invalid("The PNG ends before its IEND chunk.");
            }

            var length = BinaryPrimitives.ReadUInt32BigEndian(png[position..]);
            if (length > (uint)(png.Length - position - 12))
            {
                throw Invalid("A PNG chunk runs past the end of the data.");
            }

            var type = png.Slice(position + 4, 4);
            foreach (var value in type)
            {
                if (value is not (>= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z'))
                {
                    throw Invalid("A PNG chunk has an invalid type.");
                }
            }

            var body = png.Slice(position + 8, (int)length);
            if (Crc32.Compute(type, body) != BinaryPrimitives.ReadUInt32BigEndian(png[(position + 8 + (int)length)..]))
            {
                throw Invalid($"The {Encoding.ASCII.GetString(type)} chunk fails its CRC check.");
            }

            position += 12 + (int)length;
            return new(type, body);
        }
    }
}
