namespace TheLithium.Imprint;

/// <summary>An aggregate assertion failure with a result for every captured or unused snapshot.</summary>
public sealed class SnapshotMismatchException : SnapshotException
{
    public SnapshotReport Report
    {
        get;
    }
    public SnapshotMismatchException(SnapshotReport report)
        : base(ReportText.Format(report)) => Report = report;
}
