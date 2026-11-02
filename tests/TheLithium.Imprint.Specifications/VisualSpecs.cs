using TheLithium.Imprint;
using TheLithium.Imprint.Visual;

namespace TheLithium.Imprint.Specifications;

/// <summary>Image snapshots under Native AOT: the codec, the comparer and the full assertion path.</summary>
public static partial class Specs
{
    private static (string Name, Func<Task> Run)[] Visual =>
    [
        (nameof(PngRoundTripsUnderTheCurrentRuntime), Check.Sync(PngRoundTripsUnderTheCurrentRuntime)),
        (nameof(ImageAssertionsCreateMatchAndExplainADifference), Check.Sync(ImageAssertionsCreateMatchAndExplainADifference)),
        (nameof(AntialiasedEdgesAreIgnoredOnlyWhenAsked), Check.Sync(AntialiasedEdgesAreIgnoredOnlyWhenAsked))
    ];

    private static void PngRoundTripsUnderTheCurrentRuntime()
    {
        var pixels = new Rgba32[31 * 17];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new((byte)(i * 7), (byte)(i * 13), (byte)(i * 29), (byte)(255 - i % 200));
        }

        var image = RgbaImage.FromPixels(31, 17, pixels);
        var png = image.ToPng();
        Check.True(RgbaImage.FromPng(png).PixelsEqual(image), "The PNG didn't round-trip.");
        Check.True(png.AsSpan().SequenceEqual(image.ToPng()), "The same pixels gave different bytes.");
    }

    private static void ImageAssertionsCreateMatchAndExplainADifference()
    {
        using var fixture = new Fixture();
        var screen = Filled(5, 4, Rgba32.Opaque(255, 255, 255));
        fixture.Run(() => screen.AssertImage("screen"), SnapshotUpdate.Missing);
        Check.Equal(SnapshotStatus.Matched, fixture.Run(() => screen.AssertImage("screen")).Entries.Single().Status);

        var changedPixels = screen.Pixels.ToArray();
        changedPixels[6] = Rgba32.Opaque(0, 0, 0);
        var changed = RgbaImage.FromPixels(5, 4, changedPixels);
        var failure = Check.Throws<SnapshotMismatchException>(() => fixture.Run(() => changed.AssertImage("screen")));
        Check.True(failure.Report.Entries.Single().Difference?.StartsWith("1 of 20 pixels differ", StringComparison.Ordinal) == true);
        var diff = Path.Combine(failure.Report.ArtifactDirectory ?? "", "screen.diff.png");
        Check.Equal(Rgba32.Opaque(255, 0, 0), RgbaImage.FromPng(File.ReadAllBytes(diff))[1, 1]);
    }

    private static void AntialiasedEdgesAreIgnoredOnlyWhenAsked()
    {
        var edge = new Rgba32[8 * 8];
        for (var i = 0; i < edge.Length; i++)
        {
            edge[i] = i % 8 < 4 ? Rgba32.Opaque(0, 0, 0) : Rgba32.Opaque(255, 255, 255);
        }

        var expected = RgbaImage.FromPixels(8, 8, edge);
        edge[4 * 8 + 3] = Rgba32.Opaque(128, 128, 128);
        var received = RgbaImage.FromPixels(8, 8, edge);
        Check.True(!DefaultImageComparer.Instance.Compare(expected, received, ResolvedImageComparison.Defaults).Equal);
        var tolerant = ResolvedImageComparison.Defaults.Apply(new() { IgnoreAntialiasing = true });
        Check.True(DefaultImageComparer.Instance.Compare(expected, received, tolerant).Equal);
    }

    private static RgbaImage Filled(int width, int height, Rgba32 color)
        => RgbaImage.FromPixels(width, height, Enumerable.Repeat(color, width * height).ToArray());
}
