namespace TheLithium.Imprint.Execution.Models;

/// <summary>
/// One staged capture. A text capture uses <see cref="Format"/>, <see cref="Comparison"/> and <see cref="Comparer"/>.
/// A binary capture carries its own <see cref="BinaryComparer"/> instead.
/// </summary>
internal sealed record CapturedValue(string Name, string FileName, SnapshotFile Content,
    SnapshotFormat Format, SnapshotUpdate Update, ResolvedSnapshotComparison Comparison, ISnapshotComparer? Comparer)
{
    internal IBinarySnapshotComparer? BinaryComparer
    {
        get; init;
    }
}
