namespace TheLithium.Imprint.Comparison.Models;

/// <summary>A binary comparison outcome, with files that explain a difference, such as a diff image.</summary>
internal sealed record BinaryComparisonResult(bool Equal, string? Difference, IReadOnlyList<SnapshotAttachment> Attachments)
{
    internal static BinaryComparisonResult Match { get; } = new(true, null, []);
}
