namespace TheLithium.Imprint;

/// <summary>An aggregate assertion failure with a result for every captured or unused snapshot.</summary>
public sealed class SnapshotMismatchException : SnapshotException
{
    /// <summary>Structured results for every captured and unused snapshot.</summary>
    public SnapshotReport Report
    {
        get;
    }
    /// <summary>Creates a failure from an aggregate snapshot report.</summary>
    /// <param name="report">The report describing the mismatch.</param>
    public SnapshotMismatchException(SnapshotReport report)
        : base(FormatReport(report)) => Report = report;

    private static string FormatReport(SnapshotReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return ReportText.Format(report);
    }
}
