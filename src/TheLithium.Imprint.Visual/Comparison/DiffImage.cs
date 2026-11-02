using TheLithium.Imprint.Configuration;

namespace TheLithium.Imprint.Visual.Comparison;

/// <summary>Builds the diff image a failed comparison writes as <c>&lt;name&gt;.diff.png</c>.</summary>
internal static class DiffImage
{
    internal static readonly Rgba32 Changed = Rgba32.Opaque(255, 0, 0);
    internal static readonly Rgba32 Antialiased = Rgba32.Opaque(255, 200, 0);
    internal static readonly Rgba32 Ignored = Rgba32.Opaque(120, 170, 255);
    private static readonly Rgba32 Padding = Rgba32.Opaque(255, 0, 255);

    /// <summary>The received image faded to light gray, with each pixel's outcome drawn over it.</summary>
    internal static RgbaImage FromOutcomes(RgbaImage received, ReadOnlySpan<PixelOutcome> outcomes)
    {
        var source = received.Pixels;
        var pixels = new Rgba32[source.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = outcomes[i] switch
            {
                PixelOutcome.Same => Faded(source[i]),
                PixelOutcome.Changed => Changed,
                PixelOutcome.Antialiased => Antialiased,
                PixelOutcome.Ignored => Ignored,
                _ => throw new ArgumentOutOfRangeException(nameof(outcomes), outcomes[i], null)
            };
        }
        return new(received.Width, received.Height, pixels);
    }

    /// <summary>Both images next to each other on magenta, for a size mismatch where pixels can't be matched up.</summary>
    internal static RgbaImage? SideBySide(RgbaImage expected, RgbaImage received)
    {
        const int gap = 8;
        var width = (long)expected.Width + gap + received.Width;
        var height = Math.Max(expected.Height, received.Height);
        if (width * height > SnapshotLimits.MaximumImagePixels)
        {
            return null;
        }

        var pixels = new Rgba32[width * height];
        pixels.AsSpan().Fill(Padding);
        Draw(pixels, (int)width, expected, 0);
        Draw(pixels, (int)width, received, expected.Width + gap);
        return new((int)width, height, pixels);
    }

    private static void Draw(Rgba32[] target, int width, RgbaImage image, int left)
    {
        for (var y = 0; y < image.Height; y++)
        {
            image.Pixels.Slice(y * image.Width, image.Width).CopyTo(target.AsSpan(y * width + left, image.Width));
        }
    }

    private static Rgba32 Faded(Rgba32 pixel)
    {
        var luma = (pixel.R * 299 + pixel.G * 587 + pixel.B * 114) / 1000 * pixel.A / 255 + 255 - pixel.A;
        var light = (byte)(255 - (255 - luma) / 4);
        return Rgba32.Opaque(light, light, light);
    }
}

internal enum PixelOutcome : byte
{
    Same, Changed, Antialiased, Ignored
}
