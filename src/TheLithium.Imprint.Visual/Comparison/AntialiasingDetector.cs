namespace TheLithium.Imprint.Visual.Comparison;

/// <summary>
/// Finds pixels that look like anti-aliased edges, after V. Vysniauskas, "Anti-aliased Pixel and Intensity Slope
/// Detector" (2009). An edge pixel has both a darker and a brighter neighbor, few neighbors equal to it, and its darkest
/// or brightest neighbor sits in a flat area of both images. A changed glyph or a moved line fails that last test.
/// </summary>
internal static class AntialiasingDetector
{
    internal static bool IsAntialiased(RgbaImage image, int x, int y, RgbaImage other)
    {
        var pixels = image.Pixels;
        var width = image.Width;
        var left = Math.Max(x - 1, 0);
        var top = Math.Max(y - 1, 0);
        var right = Math.Min(x + 1, width - 1);
        var bottom = Math.Min(y + 1, image.Height - 1);
        var center = pixels[y * width + x];
        // A pixel on the image border counts one missing neighbor as equal.
        var equal = x == left || x == right || y == top || y == bottom ? 1 : 0;
        double darkest = 0, brightest = 0;
        int darkX = 0, darkY = 0, brightX = 0, brightY = 0;
        for (var nx = left; nx <= right; nx++)
        {
            for (var ny = top; ny <= bottom; ny++)
            {
                if (nx == x && ny == y)
                {
                    continue;
                }

                var delta = BrightnessDelta(center, pixels[ny * width + nx]);
                if (delta == 0)
                {
                    if (++equal > 2)
                    {
                        return false;
                    }
                }
                else if (delta < darkest)
                {
                    (darkest, darkX, darkY) = (delta, nx, ny);
                }
                else if (delta > brightest)
                {
                    (brightest, brightX, brightY) = (delta, nx, ny);
                }
            }
        }

        if (darkest == 0 || brightest == 0)
        {
            return false;
        }

        return HasManySiblings(image, darkX, darkY) && HasManySiblings(other, darkX, darkY)
            || HasManySiblings(image, brightX, brightY) && HasManySiblings(other, brightX, brightY);
    }

    /// <summary>True when more than two neighbors have exactly the pixel's color, which marks a flat area.</summary>
    private static bool HasManySiblings(RgbaImage image, int x, int y)
    {
        var pixels = image.Pixels;
        var width = image.Width;
        var left = Math.Max(x - 1, 0);
        var top = Math.Max(y - 1, 0);
        var right = Math.Min(x + 1, width - 1);
        var bottom = Math.Min(y + 1, image.Height - 1);
        var center = pixels[y * width + x];
        var equal = x == left || x == right || y == top || y == bottom ? 1 : 0;
        for (var nx = left; nx <= right; nx++)
        {
            for (var ny = top; ny <= bottom; ny++)
            {
                if ((nx != x || ny != y) && pixels[ny * width + nx] == center && ++equal > 2)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>The difference in luma of two pixels, each blended over white first.</summary>
    private static double BrightnessDelta(Rgba32 first, Rgba32 second) => first == second ? 0 : Luma(first) - Luma(second);

    private static double Luma(Rgba32 pixel)
    {
        var alpha = pixel.A / 255.0;
        return Blend(pixel.R, alpha) * 0.29889531 + Blend(pixel.G, alpha) * 0.58662247 + Blend(pixel.B, alpha) * 0.11448223;
    }

    private static double Blend(byte channel, double alpha) => 255 + (channel - 255) * alpha;
}
