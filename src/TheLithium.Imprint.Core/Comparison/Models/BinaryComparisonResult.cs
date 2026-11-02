namespace TheLithium.Imprint.Comparison.Models;

/// <summary>
/// A binary comparison outcome, with files that explain a difference, such as a diff image. <see cref="Identical"/> means
/// the decoded content is the same, e.g. the same pixels in a differently compressed PNG, so an update keeps the stored file.
/// </summary>
internal sealed record BinaryComparisonResult(bool Equal, string? Difference, IReadOnlyList<SnapshotAttachment> Attachments)
{
    internal static BinaryComparisonResult Same { get; } = new(true, null, []) { Identical = true };

    internal bool Identical
    {
        get; init;
    }
}
