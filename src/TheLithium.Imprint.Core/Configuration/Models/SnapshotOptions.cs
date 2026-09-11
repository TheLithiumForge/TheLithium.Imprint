namespace TheLithium.Imprint;

/// <summary>Overrides for one AssertSnapshot or UpdateSnapshot call.</summary>
/// <remarks>These options affect only this capture. They do not change the policy for other snapshots or prune unused entries.</remarks>
public sealed record SnapshotOptions
{
    /// <summary>Use the test policy by default. Verify forbids writes; Missing creates; All replaces this entry on success.</summary>
    public SnapshotUpdate Update { get; init; } = SnapshotUpdate.Inherit;
    /// <summary>Use Auto for plain text strings or structured JSON. Select Json to parse a string as JSON.</summary>
    public SnapshotFormat Format { get; init; } = SnapshotFormat.Auto;
    /// <summary>Replaces the inherited comparison rules as a whole. Equality rules never rewrite the captured representation.</summary>
    public SnapshotComparison? Comparison
    {
        get; init;
    }
    /// <summary>Custom equality implementation for this capture. Returning a difference participates in the normal diff report.</summary>
    public ISnapshotComparer? Comparer
    {
        get; init;
    }
}
