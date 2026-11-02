namespace TheLithium.Imprint.Configuration.Models;

/// <summary>The <c>visual</c> group of snapshots.config.json, read by the same strict reader as every other group.</summary>
internal sealed class VisualFileConfiguration
{
    public VisualCaptureSource CaptureSource { get; set; } = VisualCaptureSource.External;
    public int MaxBytesPerImage { get; set; } = SnapshotLimits.DefaultImageBytes;
    public int MaxPixelsPerImage { get; set; } = SnapshotLimits.DefaultImagePixels;
    public ImageComparisonFileConfiguration Comparison { get; set; } = new();
    public StabilityFileConfiguration Stability { get; set; } = new();
    public Dictionary<string, VisualPlatformFileConfiguration> Platforms { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class ImageComparisonFileConfiguration
{
    public int? ChannelTolerance
    {
        get; set;
    }
    public decimal? MaxDifferingPercent
    {
        get; set;
    }
    public bool? IgnoreAntialiasing
    {
        get; set;
    }
}

internal sealed class StabilityFileConfiguration
{
    public int? MatchingFrames
    {
        get; set;
    }
    public int? IntervalMilliseconds
    {
        get; set;
    }
    public int? MinimumDurationMilliseconds
    {
        get; set;
    }
    public int? TimeoutSeconds
    {
        get; set;
    }
}

internal sealed class VisualPlatformFileConfiguration
{
    public ImageComparisonFileConfiguration Comparison { get; set; } = new();
}
