using System.Buffers.Binary;
using Xunit;

namespace TheLithium.Imprint.Visual.Tests;

public sealed class PngTests
{
    [Fact]
    public void ImagesRoundTripThroughTheEncoder()
    {
        var image = Noise(37, 23, seed: 7);
        var png = image.ToPng();
        Assert.True(RgbaImage.FromPng(png).PixelsEqual(image));
        Assert.Equal(png, image.ToPng());
    }

    [Fact]
    public void LargeImagesSpanSeveralDataChunks()
    {
        var image = Noise(400, 300, seed: 3);
        Assert.True(RgbaImage.FromPng(image.ToPng()).PixelsEqual(image));
    }

    [Theory]
    [InlineData(0), InlineData(1), InlineData(2), InlineData(3), InlineData(4)]
    public void EveryFilterTypeDecodes(int filter)
    {
        int[][] rows = [[10, 20, 30, 255, 200, 100, 50, 128, 0, 0, 0, 0], [11, 22, 33, 250, 190, 90, 40, 120, 255, 255, 255, 255]];
        var image = RgbaImage.FromPng(TestPng.Write(3, 2, colorType: 6, depth: 8, rows, filter));
        Assert.Equal(new Rgba32(200, 100, 50, 128), image[1, 0]);
        Assert.Equal(new Rgba32(255, 255, 255, 255), image[2, 1]);
        Assert.Equal(new Rgba32(11, 22, 33, 250), image[0, 1]);
    }

    [Theory]
    [InlineData(1, new[] { 0, 1 }, new byte[] { 0, 255 })]
    [InlineData(2, new[] { 0, 1, 2, 3 }, new byte[] { 0, 85, 170, 255 })]
    [InlineData(4, new[] { 0, 5, 15 }, new byte[] { 0, 85, 255 })]
    [InlineData(8, new[] { 0, 128, 255 }, new byte[] { 0, 128, 255 })]
    [InlineData(16, new[] { 0, 32896, 65535 }, new byte[] { 0, 128, 255 })]
    public void GrayscaleScalesEveryDepthToEightBits(int depth, int[] samples, byte[] expected)
    {
        var image = RgbaImage.FromPng(TestPng.Write(samples.Length, 1, colorType: 0, depth, [samples]));
        Assert.Equal(expected.Select(value => Rgba32.Opaque(value, value, value)), image.Pixels.ToArray());
    }

    [Fact]
    public void SixteenBitChannelsRoundToTheNearestValue()
    {
        int[][] rows = [[0x0080, 0x7FFF, 0xFFFF, 0x8080]];
        var image = RgbaImage.FromPng(TestPng.Write(1, 1, colorType: 6, depth: 16, rows));
        Assert.Equal(new Rgba32(0, 127, 255, 128), image[0, 0]);
    }

    [Fact]
    public void GrayAlphaAndRgbDecode()
    {
        Assert.Equal(new Rgba32(90, 90, 90, 40), RgbaImage.FromPng(TestPng.Write(1, 1, 4, 8, [[90, 40]]))[0, 0]);
        Assert.Equal(Rgba32.Opaque(1, 2, 3), RgbaImage.FromPng(TestPng.Write(1, 1, 2, 8, [[1, 2, 3]]))[0, 0]);
        Assert.Equal(Rgba32.Opaque(1, 2, 3), RgbaImage.FromPng(TestPng.Write(1, 1, 2, 16, [[257, 514, 771]]))[0, 0]);
    }

    [Theory]
    [InlineData(1), InlineData(2), InlineData(4), InlineData(8)]
    public void PalettesDecodeWithTheirTransparency(int depth)
    {
        byte[] palette = [255, 0, 0, 0, 255, 0];
        var image = RgbaImage.FromPng(TestPng.Write(2, 1, colorType: 3, depth, [[1, 0]], palette: palette, transparency: [128]));
        Assert.Equal(Rgba32.Opaque(0, 255, 0), image[0, 0]);
        Assert.Equal(new Rgba32(255, 0, 0, 128), image[1, 0]);
    }

    [Fact]
    public void ColorKeyTransparencyApplies()
    {
        var gray = RgbaImage.FromPng(TestPng.Write(2, 1, 0, 8, [[7, 8]], transparency: [0, 7]));
        Assert.Equal(new Rgba32(7, 7, 7, 0), gray[0, 0]);
        Assert.Equal(Rgba32.Opaque(8, 8, 8), gray[1, 0]);

        var rgb = RgbaImage.FromPng(TestPng.Write(2, 1, 2, 8, [[1, 2, 3, 4, 5, 6]], transparency: [0, 4, 0, 5, 0, 6]));
        Assert.Equal(Rgba32.Opaque(1, 2, 3), rgb[0, 0]);
        Assert.Equal(new Rgba32(4, 5, 6, 0), rgb[1, 0]);
    }

    [Fact]
    public void AncillaryChunksAreIgnoredAndTheEncoderWritesOnlyKnownOnes()
    {
        var png = TestPng.Write(1, 1, 2, 8, [[1, 2, 3]], extra: [("gAMA", [0, 0, 0xB1, 0x8F]), ("tEXt", "Comment\0x"u8.ToArray())]);
        Assert.Equal(Rgba32.Opaque(1, 2, 3), RgbaImage.FromPng(png)[0, 0]);

        var written = RgbaImage.FromPng(png).ToPng();
        Assert.Equal(["IHDR", "sRGB", "IDAT", "IEND"], ChunkNames(written));
    }

    [Theory]
    [InlineData("signature", "isn't a PNG")]
    [InlineData("crc", "fails its CRC check")]
    [InlineData("truncated", "ends before its IEND chunk")]
    [InlineData("critical", "critical chunk this reader doesn't know: ABCD")]
    [InlineData("interlaced", "Interlaced PNGs aren't supported")]
    [InlineData("depth", "bit depth 3")]
    [InlineData("split", "IDAT chunks aren't consecutive")]
    [InlineData("short", "shorter than its header says")]
    [InlineData("long", "longer than its header says")]
    [InlineData("palette", "palette entry 2")]
    [InlineData("first", "doesn't start with an IHDR chunk")]
    public void InvalidPngsFailWithTheReason(string defect, string message)
    {
        var failure = Assert.Throws<SnapshotCaptureException>(() => RgbaImage.FromPng(Broken(defect)));
        Assert.Contains(message, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePixelLimitAppliesBeforeDecoding()
    {
        var png = TestPng.Write(4, 4, 0, 8, [[0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0]]);
        var failure = Assert.Throws<SnapshotCaptureException>(() => RgbaImage.FromPng(png, maxPixels: 15));
        Assert.Contains("more than the 15-pixel limit", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CropCopiesOneArea()
    {
        var image = Noise(5, 4, seed: 1);
        var crop = image.Crop(new(1, 2, 3, 2));
        Assert.Equal(3, crop.Width);
        Assert.Equal(image[1, 2], crop[0, 0]);
        Assert.Equal(image[3, 3], crop[2, 1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Crop(new(3, 0, 3, 1)));
    }

    internal static RgbaImage Noise(int width, int height, int seed)
    {
        var random = new Random(seed);
        var pixels = new Rgba32[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
        }
        return RgbaImage.FromPixels(width, height, pixels);
    }

    private static byte[] Broken(string defect)
    {
        var good = TestPng.Write(2, 1, 2, 8, [[1, 2, 3, 4, 5, 6]]);
        switch (defect)
        {
            case "signature":
                good[1] = (byte)'X';
                return good;
            case "crc":
                // Bytes 29 to 32 are the IHDR chunk's CRC.
                good[29] ^= 0xFF;
                return good;
            case "truncated":
                return good[..^12];
            case "critical":
                return Insert(good, "ABCD", [1]);
            case "interlaced":
                return Header(width: 1, depth: 8, colorType: 2, interlace: 1);
            case "depth":
                return Header(width: 1, depth: 3, colorType: 0, interlace: 0);
            case "split":
                var data = TestPng.Deflate([0, 1, 2, 3]);
                return Assemble(Ihdr(1, 8, 2, 0), ("IDAT", data[..3]), ("tIME", new byte[7]), ("IDAT", data[3..]));
            case "short":
                return Assemble(Ihdr(2, 8, 2, 0), ("IDAT", TestPng.Deflate([0, 1, 2, 3])));
            case "long":
                return Assemble(Ihdr(1, 8, 2, 0), ("IDAT", TestPng.Deflate([0, 1, 2, 3, 9, 9])));
            case "palette":
                return TestPng.Write(1, 1, 3, 8, [[2]], palette: [1, 2, 3, 4, 5, 6]);
            case "first":
                return Assemble(("gAMA", [0, 0, 0xB1, 0x8F]), ("IHDR", Ihdr(1, 8, 2, 0).Body));
            default:
                throw new ArgumentOutOfRangeException(nameof(defect));
        }
    }

    private static byte[] Header(int width, int depth, int colorType, int interlace)
        => Assemble(Ihdr(width, depth, colorType, interlace), ("IDAT", TestPng.Deflate([0, 0, 0, 0])));

    private static (string Type, byte[] Body) Ihdr(int width, int depth, int colorType, int interlace)
    {
        var body = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(body, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(4), 1);
        body[8] = (byte)depth;
        body[9] = (byte)colorType;
        body[12] = (byte)interlace;
        return ("IHDR", body);
    }

    private static byte[] Assemble(params (string Type, byte[] Body)[] chunks)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        foreach (var (type, body) in chunks)
        {
            TestPng.Chunk(output, type, body);
        }
        TestPng.Chunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>Inserts a chunk right after IHDR, which ends 33 bytes into every PNG.</summary>
    private static byte[] Insert(byte[] png, string type, byte[] body)
    {
        using var chunk = new MemoryStream();
        TestPng.Chunk(chunk, type, body);
        return [.. png[..33], .. chunk.ToArray(), .. png[33..]];
    }

    private static string[] ChunkNames(byte[] png)
    {
        var names = new List<string>();
        for (var position = 8; position < png.Length;)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position));
            names.Add(System.Text.Encoding.ASCII.GetString(png, position + 4, 4));
            position += 12 + length;
        }
        return names.Distinct().ToArray();
    }
}
