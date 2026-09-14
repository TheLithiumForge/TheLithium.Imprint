namespace TheLithium.Imprint;

/// <summary>The equality decision and optional human-readable explanation from a comparer.</summary>
/// <param name="Equal">True when the expected and captured values match.</param>
/// <param name="Difference">An explanation, JSON path, or diff to include when they differ.</param>
public sealed record SnapshotComparisonResult(bool Equal, string? Difference = null)
{
    /// <summary>A reusable successful equality result.</summary>
    public static SnapshotComparisonResult Match { get; } = new(true);
}
