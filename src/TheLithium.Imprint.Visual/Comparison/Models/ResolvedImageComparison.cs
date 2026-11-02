namespace TheLithium.Imprint.Visual;

/// <summary>The comparison rules a comparer receives. Every field has a value. Omitted fields use the defaults.</summary>
public sealed record ResolvedImageComparison
{
    private ResolvedImageComparison()
    {
    }

    /// <summary>An exact comparison: no tolerance, no differing pixels, no anti-aliasing allowance and no regions.</summary>
    public static ResolvedImageComparison Defaults { get; } = new();

    public int ChannelTolerance
    {
        get; private init;
    }

    public decimal MaxDifferingPercent
    {
        get; private init;
    }

    public bool IgnoreAntialiasing
    {
        get; private init;
    }

    public IReadOnlyList<PixelRect> IgnoreRegions { get; private init; } = [];

    /// <summary>Applies a patch. Null fields keep their current values.</summary>
    public ResolvedImageComparison Apply(ImageComparison? patch)
    {
        if (patch is null)
        {
            return this;
        }

        var result = this with
        {
            ChannelTolerance = patch.ChannelTolerance ?? ChannelTolerance,
            MaxDifferingPercent = patch.MaxDifferingPercent ?? MaxDifferingPercent,
            IgnoreAntialiasing = patch.IgnoreAntialiasing ?? IgnoreAntialiasing,
            IgnoreRegions = patch.IgnoreRegions?.ToArray() ?? IgnoreRegions
        };
        result.Validate();
        return result;
    }

    /// <summary>The comparison as a patch, for passing it on.</summary>
    public ImageComparison ToOptions() => new()
    {
        ChannelTolerance = ChannelTolerance,
        MaxDifferingPercent = MaxDifferingPercent,
        IgnoreAntialiasing = IgnoreAntialiasing,
        IgnoreRegions = IgnoreRegions
    };

    private void Validate()
    {
        if (ChannelTolerance is < 0 or > 255)
        {
            throw new SnapshotConfigurationException("ChannelTolerance must be 0..255.");
        }
        if (MaxDifferingPercent is < 0 or > 100)
        {
            throw new SnapshotConfigurationException("MaxDifferingPercent must be 0..100.");
        }
        foreach (var region in IgnoreRegions)
        {
            if (region.IsEmpty || region.X < 0 || region.Y < 0)
            {
                throw new SnapshotConfigurationException($"Ignore region {region} needs a positive size and a position of at least 0,0.");
            }
        }
    }
}
