using Xunit;

namespace TheLithium.Imprint.Visual.Tests;

public sealed class ComparerTests
{
    private static readonly Rgba32 White = Rgba32.Opaque(255, 255, 255);
    private static readonly Rgba32 Black = Rgba32.Opaque(0, 0, 0);

    [Fact]
    public void TheDefaultIsExact()
    {
        var expected = Solid(10, 10, White);
        var received = With(expected, (4, 4, Rgba32.Opaque(254, 255, 255)));
        var result = Compare(expected, received);
        Assert.False(result.Equal);
        Assert.StartsWith("1 of 100 pixels differ (1 percent, limit 0). Largest channel difference 1, at 4,4.", result.Difference, StringComparison.Ordinal);
        Assert.Equal(DiffColor.Changed, Classify(result.Diff, 4, 4));
    }

    [Fact]
    public void ChannelToleranceAcceptsSmallDifferences()
    {
        var expected = Solid(4, 4, White);
        var received = With(expected, (1, 1, Rgba32.Opaque(247, 255, 255)));
        Assert.True(Compare(expected, received, new() { ChannelTolerance = 8 }).Equal);
        Assert.False(Compare(expected, received, new() { ChannelTolerance = 7 }).Equal);
    }

    [Fact]
    public void MaxDifferingPercentIsTakenOfComparedPixels()
    {
        var expected = Solid(10, 10, White);
        var received = With(expected, (0, 0, Black), (1, 0, Black));
        Assert.True(Compare(expected, received, new() { MaxDifferingPercent = 2 }).Equal);
        Assert.False(Compare(expected, received, new() { MaxDifferingPercent = 1.99m }).Equal);
    }

    [Fact]
    public void FullyTransparentPixelsAreEqualWhateverTheirColor()
    {
        var expected = Solid(2, 2, new(255, 0, 0, 0));
        var received = Solid(2, 2, new(0, 0, 255, 0));
        Assert.True(Compare(expected, received).Equal);
        Assert.False(Compare(expected, Solid(2, 2, new(0, 0, 255, 1))).Equal);
    }

    [Fact]
    public void IgnoreRegionsAreLeftOutOfTheCount()
    {
        var expected = Solid(10, 10, White);
        var received = With(expected, (8, 8, Black), (9, 9, Black));
        var result = Compare(expected, received, new() { IgnoreRegions = [new(8, 8, 2, 2)] });
        Assert.True(result.Equal);

        var partial = Compare(expected, received, new() { IgnoreRegions = [new(8, 8, 1, 1)] });
        Assert.False(partial.Equal);
        Assert.StartsWith("1 of 99 pixels differ", partial.Difference, StringComparison.Ordinal);
        Assert.Contains("1 pixels were in ignore regions.", partial.Difference, StringComparison.Ordinal);
        Assert.Equal(DiffColor.Ignored, Classify(partial.Diff, 8, 8));
    }

    [Fact]
    public void ARegionOutsideTheImageFails()
    {
        var image = Solid(4, 4, White);
        Assert.Throws<SnapshotCaptureException>(() => Compare(image, With(image, (0, 0, Black)), new() { IgnoreRegions = [new(3, 3, 2, 2)] }));
    }

    [Fact]
    public void ASoftenedEdgeIsIgnoredOnlyWhenAsked()
    {
        // A vertical black-to-white edge. The received image has one gray pixel on the edge, as a renderer that
        // anti-aliases differently would draw it.
        var expected = Edge(8, 8);
        var received = With(expected, (3, 4, Rgba32.Opaque(128, 128, 128)));
        Assert.False(Compare(expected, received).Equal);
        var ignored = Compare(expected, received, new() { IgnoreAntialiasing = true, MaxDifferingPercent = 0 });
        Assert.True(ignored.Equal);
    }

    [Fact]
    public void AMovedLineIsNotTakenForAntialiasing()
    {
        var expected = Solid(9, 9, White);
        var received = Solid(9, 9, White);
        expected = WithColumn(expected, 3, Black);
        received = WithColumn(received, 4, Black);
        var result = Compare(expected, received, new() { IgnoreAntialiasing = true });
        Assert.False(result.Equal);
        Assert.StartsWith("18 of 81 pixels differ", result.Difference, StringComparison.Ordinal);
    }

    [Fact]
    public void ASizeMismatchShowsBothImagesSideBySide()
    {
        var result = Compare(Solid(3, 2, White), Solid(4, 5, Black));
        Assert.False(result.Equal);
        Assert.Equal("Expected 3x2, received 4x5. A different size usually means a different display scale or window size.", result.Difference);
        var diff = Assert.IsType<RgbaImage>(result.Diff);
        Assert.Equal(3 + 8 + 4, diff.Width);
        Assert.Equal(5, diff.Height);
        Assert.Equal(White, diff[0, 0]);
        Assert.Equal(Black, diff[11, 4]);
        Assert.Equal(Rgba32.Opaque(255, 0, 255), diff[0, 4]);
    }

    [Fact]
    public void TheDifferenceNamesTheBoundsOfEveryChange()
    {
        var expected = Solid(10, 10, White);
        var received = With(expected, (2, 3, Black), (6, 8, Rgba32.Opaque(200, 255, 255)));
        var result = Compare(expected, received);
        Assert.Contains("Largest channel difference 255, at 2,3. The differences span 2,3 to 6,8.", result.Difference, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvedComparisonsValidateTheirPatch()
    {
        Assert.Throws<SnapshotConfigurationException>(() => ResolvedImageComparison.Defaults.Apply(new() { ChannelTolerance = 256 }));
        Assert.Throws<SnapshotConfigurationException>(() => ResolvedImageComparison.Defaults.Apply(new() { MaxDifferingPercent = -1 }));
        Assert.Throws<SnapshotConfigurationException>(() => ResolvedImageComparison.Defaults.Apply(new() { IgnoreRegions = [new(0, 0, 0, 1)] }));
        var resolved = ResolvedImageComparison.Defaults.Apply(new() { ChannelTolerance = 3 }).Apply(new() { MaxDifferingPercent = 1 });
        Assert.Equal(3, resolved.ChannelTolerance);
        Assert.Equal(1, resolved.MaxDifferingPercent);
        Assert.False(resolved.IgnoreAntialiasing);
    }

    internal static RgbaImage Solid(int width, int height, Rgba32 color)
        => RgbaImage.FromPixels(width, height, Enumerable.Repeat(color, width * height).ToArray());

    internal static RgbaImage With(RgbaImage image, params (int X, int Y, Rgba32 Color)[] changes)
    {
        var pixels = image.Pixels.ToArray();
        foreach (var (x, y, color) in changes)
        {
            pixels[y * image.Width + x] = color;
        }
        return RgbaImage.FromPixels(image.Width, image.Height, pixels);
    }

    private static RgbaImage WithColumn(RgbaImage image, int column, Rgba32 color)
        => With(image, Enumerable.Range(0, image.Height).Select(y => (column, y, color)).ToArray());

    private static RgbaImage Edge(int width, int height)
    {
        var image = Solid(width, height, White);
        return With(image, Enumerable.Range(0, height).SelectMany(y => Enumerable.Range(0, width / 2).Select(x => (x, y, Black))).ToArray());
    }

    private static ImageComparisonResult Compare(RgbaImage expected, RgbaImage received, ImageComparison? patch = null)
        => DefaultImageComparer.Instance.Compare(expected, received, ResolvedImageComparison.Defaults.Apply(patch));

    private static DiffColor Classify(RgbaImage? diff, int x, int y)
    {
        var pixel = Assert.IsType<RgbaImage>(diff)[x, y];
        if (pixel == Rgba32.Opaque(255, 0, 0))
        {
            return DiffColor.Changed;
        }
        if (pixel == Rgba32.Opaque(120, 170, 255))
        {
            return DiffColor.Ignored;
        }
        return pixel == Rgba32.Opaque(255, 200, 0) ? DiffColor.Antialiased : DiffColor.Faded;
    }

    private enum DiffColor
    {
        Faded, Changed, Antialiased, Ignored
    }
}
