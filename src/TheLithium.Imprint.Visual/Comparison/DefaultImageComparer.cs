using System.Globalization;
using System.Runtime.InteropServices;
using TheLithium.Imprint.Visual.Comparison;

namespace TheLithium.Imprint.Visual;

/// <summary>
/// Compares pixel by pixel on the largest channel difference. Two fully transparent pixels are equal whatever their
/// color. Ignore regions are left out of the count, and the allowed share of differing pixels is taken of the rest.
/// </summary>
public sealed class DefaultImageComparer : IImageComparer
{
    private DefaultImageComparer()
    {
    }

    public static DefaultImageComparer Instance { get; } = new();

    public ImageComparisonResult Compare(RgbaImage expected, RgbaImage received, ResolvedImageComparison options)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(received);
        ArgumentNullException.ThrowIfNull(options);
        if (expected.Width != received.Width || expected.Height != received.Height)
        {
            return new(false, string.Create(CultureInfo.InvariantCulture,
                $"Expected {expected.Width}x{expected.Height}, received {received.Width}x{received.Height}. A different size usually means a different display scale or window size."),
                DiffImage.SideBySide(expected, received));
        }

        var width = expected.Width;
        var ignored = IgnoredPixels(options.IgnoreRegions, width, expected.Height);
        var first = MemoryMarshal.Cast<Rgba32, uint>(expected.Pixels);
        var second = MemoryMarshal.Cast<Rgba32, uint>(received.Pixels);
        var counts = new Counts();
        PixelOutcome[]? outcomes = null;
        for (var y = 0; y < expected.Height; y++)
        {
            var start = y * width;
            if (ignored is null && first.Slice(start, width).SequenceEqual(second.Slice(start, width)))
            {
                continue;
            }

            for (var x = 0; x < width; x++)
            {
                var index = start + x;
                if (ignored is not null && ignored[index])
                {
                    continue;
                }
                if (first[index] == second[index])
                {
                    continue;
                }

                var outcome = Classify(expected, received, x, y, options, ref counts);
                if (outcome != PixelOutcome.Same)
                {
                    outcomes ??= new PixelOutcome[first.Length];
                    outcomes[index] = outcome;
                }
            }
        }

        var masked = ignored is null ? 0 : ignored.Count(value => value);
        var compared = (long)first.Length - masked;
        if (compared == 0)
        {
            return new(false, "Every pixel is inside an ignore region, so nothing was compared.");
        }

        var allowed = (long)Math.Floor(compared * options.MaxDifferingPercent / 100m);
        if (counts.Changed <= allowed)
        {
            return ImageComparisonResult.Match;
        }

        outcomes ??= new PixelOutcome[first.Length];
        if (ignored is not null)
        {
            for (var i = 0; i < ignored.Length; i++)
            {
                if (ignored[i])
                {
                    outcomes[i] = PixelOutcome.Ignored;
                }
            }
        }
        return new(false, Describe(counts, compared, masked, options), DiffImage.FromOutcomes(received, outcomes));
    }

    private static PixelOutcome Classify(RgbaImage expected, RgbaImage received, int x, int y, ResolvedImageComparison options, ref Counts counts)
    {
        var index = y * expected.Width + x;
        var a = expected.Pixels[index];
        var b = received.Pixels[index];
        if (a.A == 0 && b.A == 0)
        {
            return PixelOutcome.Same;
        }

        var delta = Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)), Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.A - b.A)));
        if (delta <= options.ChannelTolerance)
        {
            return PixelOutcome.Same;
        }

        if (options.IgnoreAntialiasing
            && (AntialiasingDetector.IsAntialiased(expected, x, y, received) || AntialiasingDetector.IsAntialiased(received, x, y, expected)))
        {
            counts.Antialiased++;
            return PixelOutcome.Antialiased;
        }

        counts.Changed++;
        counts.Bounds = PixelRect.Union(counts.Bounds, x, y);
        if (delta > counts.Largest)
        {
            (counts.Largest, counts.LargestX, counts.LargestY) = (delta, x, y);
        }
        return PixelOutcome.Changed;
    }

    private static bool[]? IgnoredPixels(IReadOnlyList<PixelRect> regions, int width, int height)
    {
        if (regions.Count == 0)
        {
            return null;
        }

        var ignored = new bool[width * height];
        foreach (var region in regions)
        {
            if (!region.FitsIn(width, height))
            {
                throw new SnapshotCaptureException($"Ignore region {region} doesn't fit inside the {width}x{height} image.");
            }
            for (var y = region.Y; y < region.Y + region.Height; y++)
            {
                ignored.AsSpan(y * width + region.X, region.Width).Fill(true);
            }
        }
        return ignored;
    }

    private static string Describe(Counts counts, long compared, long masked, ResolvedImageComparison options)
    {
        var percent = counts.Changed * 100m / compared;
        var bounds = counts.Bounds;
        var text = string.Create(CultureInfo.InvariantCulture,
            $"{counts.Changed:N0} of {compared:N0} pixels differ ({percent:0.###} percent, limit {options.MaxDifferingPercent:0.###}). Largest channel difference {counts.Largest}, at {counts.LargestX},{counts.LargestY}. The differences span {bounds.X},{bounds.Y} to {bounds.X + bounds.Width - 1},{bounds.Y + bounds.Height - 1}.");
        if (counts.Antialiased != 0)
        {
            text += string.Create(CultureInfo.InvariantCulture, $" {counts.Antialiased:N0} anti-aliased pixels were ignored.");
        }
        if (masked != 0)
        {
            text += string.Create(CultureInfo.InvariantCulture, $" {masked:N0} pixels were in ignore regions.");
        }
        return text;
    }

    private struct Counts
    {
        internal long Changed;
        internal long Antialiased;
        internal int Largest;
        internal int LargestX;
        internal int LargestY;
        internal PixelRect Bounds;
    }
}
