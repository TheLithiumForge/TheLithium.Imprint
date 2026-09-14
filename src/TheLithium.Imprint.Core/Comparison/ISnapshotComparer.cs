namespace TheLithium.Imprint;

/// <summary>Custom equality for a snapshot. Implementations must be deterministic and safe for concurrent tests.</summary>
public interface ISnapshotComparer
{
    /// <summary>Compares the stored and eagerly captured representations without changing either value.</summary>
    /// <param name="expected">The UTF-8 baseline decoded as a string.</param>
    /// <param name="received">The captured representation, already formatted for storage.</param>
    /// <param name="format">The resolved JSON or text format; never Auto.</param>
    /// <param name="options">The effective equality rules for this capture; all fields are concrete values.</param>
    /// <returns>A match or a difference to include in the aggregate assertion report.</returns>
    SnapshotComparisonResult Compare(string expected, string received, SnapshotFormat format, ResolvedSnapshotComparison options);
}
