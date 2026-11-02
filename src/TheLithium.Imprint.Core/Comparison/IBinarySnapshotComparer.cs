namespace TheLithium.Imprint.Comparison;

/// <summary>
/// Compares two binary snapshots of one format. A format package supplies it with each capture, so Core stores,
/// approves and prunes binary snapshots without knowing the format.
/// </summary>
internal interface IBinarySnapshotComparer
{
    /// <summary>Called only when the bytes differ, since identical bytes always match.</summary>
    BinaryComparisonResult Compare(byte[] expected, byte[] received, CancellationToken cancellation);
}
