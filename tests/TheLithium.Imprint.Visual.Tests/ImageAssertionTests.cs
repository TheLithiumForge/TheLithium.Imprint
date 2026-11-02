using TheLithium.Imprint.Specifications;
using Xunit;

namespace TheLithium.Imprint.Visual.Tests;

/// <summary>AssertImage through explicit fixture lifetimes, so each case controls the update policy and project file.</summary>
public sealed class ImageAssertionTests
{
    private static readonly Rgba32 White = Rgba32.Opaque(255, 255, 255);
    private static readonly Rgba32 Black = Rgba32.Opaque(0, 0, 0);

    [Fact]
    public void TheFirstRunWritesAPngNextToTheTest()
    {
        using var fixture = new Fixture();
        var screen = ComparerTests.Solid(6, 4, White);
        fixture.Run(() => screen.AssertImage(), SnapshotUpdate.Missing);
        var stored = File.ReadAllBytes(fixture.FilePath("screen.png"));
        Assert.Equal(screen.ToPng(), stored);
        Assert.True(RgbaImage.FromPng(stored).PixelsEqual(screen));
    }

    [Fact]
    public void AChangedPixelFailsWithEveryArtifact()
    {
        using var fixture = new Fixture();
        var screen = ComparerTests.Solid(6, 4, White);
        fixture.Run(() => screen.AssertImage("screen"), SnapshotUpdate.Missing);

        var changed = ComparerTests.With(screen, (2, 1, Black));
        var failure = Assert.Throws<SnapshotMismatchException>(() => fixture.Run(() => changed.AssertImage("screen")));
        var entry = Assert.Single(failure.Report.Entries);
        Assert.Equal("screen.png", entry.FileName);
        Assert.StartsWith("1 of 24 pixels differ", entry.Difference, StringComparison.Ordinal);
        var artifacts = Assert.IsType<string>(failure.Report.ArtifactDirectory);
        Assert.True(RgbaImage.FromPng(File.ReadAllBytes(Path.Combine(artifacts, "screen.received.png"))).PixelsEqual(changed));
        Assert.True(RgbaImage.FromPng(File.ReadAllBytes(Path.Combine(artifacts, "screen.expected.png"))).PixelsEqual(screen));
        var diff = RgbaImage.FromPng(File.ReadAllBytes(Path.Combine(artifacts, "screen.diff.png")));
        Assert.Equal(Rgba32.Opaque(255, 0, 0), diff[2, 1]);
    }

    [Fact]
    public void UpdateImageReplacesTheBaseline()
    {
        using var fixture = new Fixture();
        var screen = ComparerTests.Solid(3, 3, White);
        fixture.Run(() => screen.AssertImage("screen"), SnapshotUpdate.Missing);
        var changed = ComparerTests.With(screen, (0, 0, Black));
        fixture.Run(() => changed.UpdateImage("screen"));
        Assert.True(RgbaImage.FromPng(File.ReadAllBytes(fixture.FilePath("screen.png"))).PixelsEqual(changed));
    }

    [Fact]
    public void OptionsCanAllowADifference()
    {
        using var fixture = new Fixture();
        var screen = ComparerTests.Solid(10, 10, White);
        fixture.Run(() => screen.AssertImage("screen"), SnapshotUpdate.Missing);
        var changed = ComparerTests.With(screen, (5, 5, Black));
        var report = fixture.Run(() => changed.AssertImage("screen", new() { Comparison = new() { MaxDifferingPercent = 1 } }));
        Assert.Equal(SnapshotStatus.Matched, Assert.Single(report.Entries).Status);
    }

    [Fact]
    public void TheProjectFileSetsTheComparison()
    {
        using var fixture = new Fixture();
        fixture.Configure("""{ "visual": { "comparison": { "channelTolerance": 10 } } }""");
        var screen = ComparerTests.Solid(4, 4, White);
        fixture.Run(() => screen.AssertImage("screen"), SnapshotUpdate.Missing);
        var close = ComparerTests.With(screen, (1, 1, Rgba32.Opaque(250, 250, 250)));
        Assert.Equal(SnapshotStatus.Matched, Assert.Single(fixture.Run(() => close.AssertImage("screen")).Entries).Status);
        // The assertion layer applies last, over the project file.
        Assert.Throws<SnapshotMismatchException>(() => fixture.Run(() => close.AssertImage("screen", new() { Comparison = new() { ChannelTolerance = 0 } })));
    }

    [Fact]
    public void ACustomComparerSeesOnlyImagesThatDiffer()
    {
        using var fixture = new Fixture();
        var screen = ComparerTests.Solid(4, 4, White);
        fixture.Run(() => screen.AssertImage("screen"), SnapshotUpdate.Missing);
        var comparer = new RecordingComparer();
        fixture.Run(() => screen.AssertImage("screen", new() { Comparer = comparer }));
        Assert.Equal(0, comparer.Calls);

        var changed = ComparerTests.With(screen, (0, 0, Black));
        var report = fixture.Run(() => changed.AssertImage("screen", new() { Comparer = comparer }));
        Assert.Equal(1, comparer.Calls);
        Assert.Equal(SnapshotStatus.Matched, Assert.Single(report.Entries).Status);
    }

    [Fact]
    public void ARegionThatDoesNotFitFailsAtTheAssertion()
    {
        using var fixture = new Fixture();
        var screen = ComparerTests.Solid(4, 4, White);
        var failure = Assert.Throws<SnapshotCaptureException>(() => fixture.Run(
            () => screen.AssertImage("screen", new() { Comparison = new() { IgnoreRegions = [new(2, 2, 4, 4)] } }), SnapshotUpdate.Missing));
        Assert.Contains("doesn't fit inside the 4x4 image", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegionsThatCoverEverythingFail()
    {
        using var fixture = new Fixture();
        var screen = ComparerTests.Solid(4, 4, White);
        var regions = new PixelRect[] { new(0, 0, 4, 2), new(0, 2, 4, 2) };
        var failure = Assert.Throws<SnapshotCaptureException>(() => fixture.Run(
            () => screen.AssertImage("screen", new() { Comparison = new() { IgnoreRegions = regions } }), SnapshotUpdate.Missing));
        Assert.Contains("cover the whole image", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheImageLimitAppliesAtTheAssertion()
    {
        using var fixture = new Fixture();
        fixture.Configure("""{ "visual": { "maxPixelsPerImage": 10 } }""");
        var screen = ComparerTests.Solid(4, 4, White);
        var failure = Assert.Throws<SnapshotCaptureException>(() => fixture.Run(() => screen.AssertImage("screen"), SnapshotUpdate.Missing));
        Assert.Contains("more than the 10-pixel limit", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACorruptBaselineFailsAndCanBeReplaced()
    {
        using var fixture = new Fixture();
        var screen = ComparerTests.Solid(3, 3, White);
        fixture.Run(() => screen.AssertImage("screen"), SnapshotUpdate.Missing);
        File.WriteAllBytes(fixture.FilePath("screen.png"), [1, 2, 3]);
        var failure = Assert.Throws<SnapshotMismatchException>(() => fixture.Run(() => screen.AssertImage("screen")));
        Assert.StartsWith("The stored PNG can't be read", Assert.Single(failure.Report.Entries).Difference, StringComparison.Ordinal);
        fixture.Run(() => screen.AssertImage("screen"), SnapshotUpdate.All);
        Assert.True(RgbaImage.FromPng(File.ReadAllBytes(fixture.FilePath("screen.png"))).PixelsEqual(screen));
    }

    private sealed class RecordingComparer : IImageComparer
    {
        internal int Calls
        {
            get; private set;
        }

        public ImageComparisonResult Compare(RgbaImage expected, RgbaImage received, ResolvedImageComparison options)
        {
            Calls++;
            return ImageComparisonResult.Match;
        }
    }
}
