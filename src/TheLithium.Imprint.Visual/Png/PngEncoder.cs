using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace TheLithium.Imprint.Visual.Png;

/// <summary>
/// Writes RGBA8 with one fixed filter, compression level and chunk order, and no metadata beyond a constant sRGB chunk.
/// The same pixels give the same bytes on one runtime. Another zlib can compress them differently, which the
/// comparison treats as identical content, so stored baselines don't churn.
/// </summary>
internal static class PngEncoder
{
    private const int MaximumChunk = 64 * 1024;
    private const byte PaethFilter = 4;

    internal static byte[] Encode(RgbaImage image)
    {
        using var output = new MemoryStream();
        output.Write(PngDecoder.Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)image.Width);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)image.Height);
        header[8] = 8;
        header[9] = (byte)PngColorType.Rgba;
        WriteChunk(output, "IHDR"u8, header);
        // Perceptual rendering intent. It tells viewers the samples are sRGB.
        WriteChunk(output, "sRGB"u8, [0]);

        var compressed = Compress(image);
        for (var offset = 0; offset < compressed.Length || offset == 0; offset += MaximumChunk)
        {
            WriteChunk(output, "IDAT"u8, compressed.AsSpan(offset, Math.Min(MaximumChunk, compressed.Length - offset)));
        }

        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static byte[] Compress(RgbaImage image)
    {
        var rowBytes = image.Width * 4;
        var pixels = MemoryMarshal.AsBytes(image.Pixels);
        var filtered = new byte[rowBytes + 1];
        using var buffer = new MemoryStream();
        using (var deflater = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < image.Height; y++)
            {
                var row = pixels.Slice(y * rowBytes, rowBytes);
                var prior = y == 0 ? default : pixels.Slice((y - 1) * rowBytes, rowBytes);
                filtered[0] = PaethFilter;
                for (var i = 0; i < rowBytes; i++)
                {
                    var left = i >= 4 ? row[i - 4] : (byte)0;
                    var up = y == 0 ? (byte)0 : prior[i];
                    var upLeft = y == 0 || i < 4 ? (byte)0 : prior[i - 4];
                    filtered[i + 1] = (byte)(row[i] - PngDecoder.Paeth(left, up, upLeft));
                }
                deflater.Write(filtered);
            }
        }
        return buffer.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> body)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)body.Length);
        output.Write(length);
        output.Write(type);
        output.Write(body);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32.Compute(type, body));
        output.Write(crc);
    }
}
