namespace TheLithium.Imprint;

/// <summary>The aggregate result for one test execution.</summary>
/// <param name="Test">The suite, method, and optional case name.</param>
/// <param name="Success">Whether all comparisons and requested updates succeeded.</param>
/// <param name="Entries">Results for captures and unused files in stable name order.</param>
/// <param name="ArtifactDirectory">Optional folder containing received values and failure details.</param>
public sealed record SnapshotReport(string Test, bool Success, IReadOnlyList<SnapshotEntryResult> Entries, string? ArtifactDirectory = null);
