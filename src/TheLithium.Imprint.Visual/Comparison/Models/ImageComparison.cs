namespace TheLithium.Imprint.Visual;

/// <summary>
/// When two images count as equal. Each property is a patch: leave it null to inherit it from the platform, the
/// project file or the built-in default, which is an exact comparison.
/// </summary>
public sealed record ImageComparison
{
    /// <summary>The largest difference in any of R, G, B and A, out of 255, for two pixels to count as equal.</summary>
    public int? ChannelTolerance
    {
        get; init;
    }

    /// <summary>The share of compared pixels, from 0 to 100, that can differ beyond <see cref="ChannelTolerance"/>.</summary>
    public decimal? MaxDifferingPercent
    {
        get; init;
    }

    /// <summary>Ignores a differing pixel that looks like an anti-aliased edge in either image.</summary>
    public bool? IgnoreAntialiasing
    {
        get; init;
    }

    /// <summary>Areas left out of the comparison, e.g. a clock. A patch with regions replaces the inherited ones.</summary>
    public IReadOnlyList<PixelRect>? IgnoreRegions
    {
        get; init;
    }
}
