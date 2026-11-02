using TheLithium.Imprint.Comparison;
using TheLithium.Imprint.Comparison.Models;

namespace TheLithium.Imprint.Visual;

/// <summary>
/// What Core calls when a stored PNG and a new capture differ in bytes. It decodes the baseline, treats identical
/// pixels as identical content, and otherwise asks the image comparer, turning its diff into an attachment.
/// </summary>
internal sealed class ImageSnapshotComparer(RgbaImage received, ResolvedImageComparison comparison, IImageComparer comparer, int maxPixels)
    : IBinarySnapshotComparer
{
    public BinaryComparisonResult Compare(byte[] expected, byte[] receivedBytes, CancellationToken cancellation)
    {
        RgbaImage stored;
        try
        {
            stored = RgbaImage.FromPng(expected, maxPixels);
        }
        catch (SnapshotCaptureException error)
        {
            return new(false, $"The stored PNG can't be read, so it can't match: {error.Message}", []);
        }

        if (stored.PixelsEqual(received))
        {
            return BinaryComparisonResult.Same;
        }

        cancellation.ThrowIfCancellationRequested();
        var result = comparer.Compare(stored, received, comparison);
        if (result.Equal)
        {
            return new(true, null, []);
        }

        IReadOnlyList<SnapshotAttachment> attachments = result.Diff is { } diff ? [new("diff.png", diff.ToPng())] : [];
        return new(false, result.Difference ?? "The images differ.", attachments);
    }
}
