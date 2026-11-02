namespace TheLithium.Imprint.Visual;

/// <summary>
/// Decides whether two images match. Imprint calls it only when the images differ in at least one pixel, since
/// identical files and identical pixels always match.
/// </summary>
public interface IImageComparer
{
    ImageComparisonResult Compare(RgbaImage expected, RgbaImage received, ResolvedImageComparison options);
}
