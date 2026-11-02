using TheLithium.Imprint.Configuration;
using TheLithium.Imprint.Configuration.Models;
using TheLithium.Imprint.Specifications;
using Xunit;

namespace TheLithium.Imprint.Tests;

public sealed class VisualConfigurationTests
{
    [Fact]
    public void TheVisualGroupIsReadIntoTypedSettings()
    {
        using var fixture = new Fixture();
        fixture.Configure("""
            {
              "visual": {
                "maxBytesPerImage": 1000,
                "maxPixelsPerImage": 2000,
                "comparison": { "channelTolerance": 4, "maxDifferingPercent": 0.5 },
                "stability": { "matchingFrames": 2, "intervalMilliseconds": 50, "minimumDurationMilliseconds": 0, "timeoutSeconds": 3 },
                "platforms": { "android": { "comparison": { "ignoreAntialiasing": true } } }
              }
            }
            """);
        var visual = Read(fixture).Visual;
        Assert.Equal(VisualCaptureSource.External, visual.CaptureSource);
        Assert.Equal(1000, visual.MaxBytesPerImage);
        Assert.Equal(2000, visual.MaxPixelsPerImage);
        Assert.Equal(new(4, 0.5m, null), visual.Comparison);
        Assert.Equal(new(2, TimeSpan.FromMilliseconds(50), TimeSpan.Zero, TimeSpan.FromSeconds(3)), visual.Stability);
        Assert.Equal(new(null, null, true), visual.Platforms[VisualPlatformNames.Android]);
    }

    [Fact]
    public void LeftOutFieldsStayUnsetSoTheyInherit()
    {
        using var fixture = new Fixture();
        fixture.Configure("""{ "visual": {} }""");
        var visual = Read(fixture).Visual;
        Assert.Equal(new(null, null, null), visual.Comparison);
        Assert.Equal(new(null, null, null, null), visual.Stability);
        Assert.Empty(visual.Platforms);
        Assert.Equal(SnapshotLimits.DefaultImageBytes, visual.MaxBytesPerImage);
        Assert.Equal(SnapshotLimits.DefaultImagePixels, visual.MaxPixelsPerImage);
    }

    [Theory]
    [InlineData("""{ "visual": { "platforms": { "browser": {} } } }""", "visual.platforms.browser isn't a platform")]
    [InlineData("""{ "visual": { "comparison": { "channelTolerance": 256 } } }""", "visual.comparison needs channelTolerance 0..255")]
    [InlineData("""{ "visual": { "platforms": { "ios": { "comparison": { "maxDifferingPercent": 101 } } } } }""", "visual.platforms.ios.comparison needs")]
    [InlineData("""{ "visual": { "stability": { "matchingFrames": 0 } } }""", "visual.stability needs")]
    [InlineData("""{ "visual": { "maxPixelsPerImage": 0 } }""", "visual.maxPixelsPerImage must be")]
    [InlineData("""{ "visual": { "captureSource": "in-process" } }""", "Invalid configuration")]
    public void InvalidVisualSettingsAreRejected(string json, string message)
    {
        using var fixture = new Fixture();
        fixture.Configure(json);
        var failure = Assert.Throws<SnapshotConfigurationException>(() => Read(fixture));
        Assert.Contains(message, failure.Message, StringComparison.Ordinal);
    }

    private static ProjectConfiguration Read(Fixture fixture)
        => ProjectConfigurationReader.Read(Path.Combine(fixture.Root, "snapshots.config.json"), required: true);
}
