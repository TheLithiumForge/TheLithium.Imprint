namespace TheLithium.Imprint;

/// <summary>The aggregate result for one test execution.</summary>
public sealed record SnapshotReport
{
    /// <summary>Creates a report for one test execution.</summary>
    /// <param name="Test">The suite, method, and optional case name.</param>
    /// <param name="Success">Whether all comparisons and requested updates succeeded.</param>
    /// <param name="Entries">Results for captures and unused files in stable name order.</param>
    /// <param name="ArtifactDirectory">Optional folder containing received values and failure details.</param>
    public SnapshotReport(string Test, bool Success, IReadOnlyList<SnapshotEntryResult> Entries,
        string? ArtifactDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Test);
        ArgumentNullException.ThrowIfNull(Entries);
        if (ArtifactDirectory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ArtifactDirectory);
        }

        this.Test = Test;
        this.Success = Success;
        this.Entries = Entries;
        this.ArtifactDirectory = ArtifactDirectory;
    }

    /// <summary>The suite, method, and optional case name.</summary>
    public string Test
    {
        get; init;
    }
    /// <summary>Whether all comparisons and requested updates succeeded.</summary>
    public bool Success
    {
        get; init;
    }
    /// <summary>Results for captures and unused files in stable name order.</summary>
    public IReadOnlyList<SnapshotEntryResult> Entries
    {
        get; init;
    }
    /// <summary>Optional folder containing received values and failure details.</summary>
    public string? ArtifactDirectory
    {
        get; init;
    }

    /// <summary>Deconstructs the report into its result fields.</summary>
    public void Deconstruct(out string test, out bool success, out IReadOnlyList<SnapshotEntryResult> entries,
        out string? artifactDirectory)
        => (test, success, entries, artifactDirectory) = (Test, Success, Entries, ArtifactDirectory);
}
