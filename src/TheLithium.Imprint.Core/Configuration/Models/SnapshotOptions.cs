namespace TheLithium.Imprint;

/// <summary>Overrides for one AssertSnapshot or UpdateSnapshot call.</summary>
/// <remarks>These options affect only this capture. They do not change the policy for other snapshots or prune unused entries.</remarks>
public sealed record SnapshotOptions
{
    /// <summary>Use the test policy by default. Verify forbids writes; Missing creates; All replaces this entry on success.</summary>
    public SnapshotUpdate Update { get; init; } = SnapshotUpdate.Inherit;
    /// <summary>Use Auto for plain text strings or structured JSON. Json captures an ordinary string as a JSON string value.</summary>
    public SnapshotFormat Format { get; init; } = SnapshotFormat.Auto;
    /// <summary>Interpret a root string as a value or an already serialized JSON document.</summary>
    public SnapshotStringContent? StringContent
    {
        get; init;
    }
    /// <summary>Representation preferences for this capture; unset categories inherit test defaults.</summary>
    public SnapshotRepresentationOptions? Representation
    {
        get; init;
    }
    /// <summary>Overrides individual inherited comparison fields. Equality never rewrites the captured representation.</summary>
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
