namespace TheLithium.Imprint.Visual;

/// <summary>Overrides for one image assertion. Unset values inherit from the test, the platform and the project.</summary>
public sealed record ImageSnapshotOptions
{
    public SnapshotUpdate Update { get; init; } = SnapshotUpdate.Inherit;

    /// <summary>A comparison patch applied last, over every inherited layer.</summary>
    public ImageComparison? Comparison
    {
        get; init;
    }

    /// <summary>Replaces the pixel comparison for this image.</summary>
    public IImageComparer? Comparer
    {
        get; init;
    }
}
