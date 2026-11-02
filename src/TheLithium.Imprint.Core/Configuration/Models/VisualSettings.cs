namespace TheLithium.Imprint.Configuration.Models;

/// <summary>
/// The validated <c>visual</c> group. Core reads it so the project file has one strict reader. The visual packages
/// apply it, field by field, under their own code-level options.
/// </summary>
internal sealed record VisualSettings
{
    internal VisualCaptureSource CaptureSource { get; init; } = VisualCaptureSource.External;
    internal int MaxBytesPerImage { get; init; } = SnapshotLimits.DefaultImageBytes;
    internal int MaxPixelsPerImage { get; init; } = SnapshotLimits.DefaultImagePixels;
    internal ImageComparisonSettings Comparison { get; init; } = new(null, null, null);
    internal CaptureStabilitySettings Stability { get; init; } = new(null, null, null, null);

    /// <summary>Comparison per platform, keyed by a name from <see cref="VisualPlatformNames"/>.</summary>
    internal IReadOnlyDictionary<string, ImageComparisonSettings> Platforms { get; init; } = new Dictionary<string, ImageComparisonSettings>();
}

/// <summary>A comparison patch. Null fields inherit.</summary>
internal sealed record ImageComparisonSettings(int? ChannelTolerance, decimal? MaxDifferingPercent, bool? IgnoreAntialiasing);

/// <summary>A stability patch. Null fields inherit.</summary>
internal sealed record CaptureStabilitySettings(int? MatchingFrames, TimeSpan? Interval, TimeSpan? MinimumDuration, TimeSpan? Timeout);
