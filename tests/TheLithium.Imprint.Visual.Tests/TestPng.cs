using System.Buffers.Binary;
using System.IO.Compression;

namespace TheLithium.Imprint.Visual.Tests;

/// <summary>
/// An independent PNG writer for decoder fixtures. It writes any color type, bit depth and filter from raw samples,
/// so the decoder is checked against the specification instead of against its own encoder.
/// </summary>
internal static class TestPng
{
    internal static byte[] Write(int width, int height, int colorType, int depth, int[][] rows, int filter = 0,
        byte[]? palette = null, byte[]? transparency = null, IEnumerable<(string Type, byte[] Body)>? extra = null)
    {
        var channels = colorType switch { 0 or 3 => 1, 4 => 2, 2 => 3, 6 => 4, _ => throw new ArgumentOutOfRangeException(nameof(colorType)) };
        var rowBytes = (width * channels * depth + 7) / 8;
        var bpp = Math.Max(1, channels * depth / 8);
        var raw = new List<byte>();
        var previous = new byte[rowBytes];
        foreach (var samples in rows)
        {
            var row = Pack(samples, depth, rowBytes);
            raw.Add((byte)filter);
            raw.AddRange(Filter(row, previous, bpp, filter));
            previous = row;
        }

        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = (byte)depth;
        header[9] = (byte)colorType;
        Chunk(output, "IHDR", header);
        foreach (var (type, body) in extra ?? [])
        {
            Chunk(output, type, body);
        }
        if (palette is not null)
        {
            Chunk(output, "PLTE", palette);
        }
        if (transparency is not null)
        {
            Chunk(output, "tRNS", transparency);
        }
        Chunk(output, "IDAT", Deflate(raw.ToArray()));
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    internal static byte[] Deflate(byte[] raw)
    {
        using var buffer = new MemoryStream();
        using (var deflater = new ZLibStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            deflater.Write(raw);
        }
        return buffer.ToArray();
    }

    internal static void Chunk(Stream output, string type, byte[] body)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)body.Length);
        output.Write(length);
        output.Write(typeBytes);
        output.Write(body);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc([.. typeBytes, .. body]));
        output.Write(crc);
    }

    /// <summary>Bitwise CRC-32, written without a table, as an oracle for the decoder's table version.</summary>
    internal static uint Crc(byte[] data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }
        return ~crc;
    }

    private static byte[] Pack(int[] samples, int depth, int rowBytes)
    {
        var row = new byte[rowBytes];
        for (var i = 0; i < samples.Length; i++)
        {
            switch (depth)
            {
                case 8:
                    row[i] = (byte)samples[i];
                    break;
                case 16:
                    BinaryPrimitives.WriteUInt16BigEndian(row.AsSpan(i * 2), (ushort)samples[i]);
                    break;
                default:
                    var bit = i * depth;
                    row[bit / 8] |= (byte)(samples[i] << (8 - depth - bit % 8));
                    break;
            }
        }
        return row;
    }

    private static byte[] Filter(byte[] row, byte[] previous, int bpp, int filter)
    {
        var output = new byte[row.Length];
        for (var i = 0; i < row.Length; i++)
        {
            int left = i >= bpp ? row[i - bpp] : 0;
            int up = previous[i];
            int upLeft = i >= bpp ? previous[i - bpp] : 0;
            var predictor = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => PaethOracle(left, up, upLeft),
                _ => throw new ArgumentOutOfRangeException(nameof(filter))
            };
            output[i] = (byte)(row[i] - predictor);
        }
        return output;
    }

    private static int PaethOracle(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
        {
            return a;
        }
        return pb <= pc ? b : c;
    }
}
