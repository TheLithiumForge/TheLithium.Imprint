namespace TheLithium.Imprint.Reporting;

internal static class ReportText
{
    public static string Format(SnapshotReport report)
    {
        var lines = new List<string> { $"Snapshot mismatch: {report.Test}", "" };
        foreach (var entry in report.Entries.Where(x => x.Status is SnapshotStatus.Missing
                     or SnapshotStatus.Changed or SnapshotStatus.Unused))
        {
            lines.Add($"{entry.FileName}: {SnapshotStatusText.Format(entry.Status)}");
            if (entry.Difference is not null)
            {
                lines.Add($"  {entry.Difference.Replace("\n", "\n  ")}");
            }
        }
        if (report.ArtifactDirectory is not null)
        {
            lines.Add("");
            lines.Add($"Received files: {report.ArtifactDirectory}");
        }
        if (report.ArtifactError is not null)
        {
            lines.Add($"Failure artifacts could not be written: {report.ArtifactError}");
        }
        lines.Add("");
        lines.Add("Review the differences, then authorize updates with a method/class attribute,");
        lines.Add("UpdateSnapshot(), or the update setting in snapshots.config.json.");
        return string.Join(Environment.NewLine, lines);
    }
}
