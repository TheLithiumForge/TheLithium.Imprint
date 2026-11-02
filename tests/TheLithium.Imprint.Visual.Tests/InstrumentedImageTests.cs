using Xunit;

namespace TheLithium.Imprint.Visual.Tests;

/// <summary>
/// Ordinary tests that call only AssertImage. The build task wraps them through the capture marker, and their
/// baselines are committed next to this file, so the packaged lanes check the whole path.
/// </summary>
public sealed class InstrumentedImageTests
{
    [Fact]
    public void Checkerboard()
    {
        var pixels = new Rgba32[8 * 8];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                pixels[y * 8 + x] = (x + y) % 2 == 0 ? Rgba32.Opaque(30, 60, 90) : new Rgba32(250, 240, 230, 200);
            }
        }

        RgbaImage.FromPixels(8, 8, pixels).AssertImage("checkerboard");
    }

    [Theory]
    [InlineData(1), InlineData(2)]
    public void Bars(int count)
    {
        var pixels = new Rgba32[6 * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = i % 6 < count ? Rgba32.Opaque(200, 0, 0) : Rgba32.Opaque(0, 0, 200);
        }

        RgbaImage.FromPixels(6, 4, pixels).AssertImage("bars");
    }
}
