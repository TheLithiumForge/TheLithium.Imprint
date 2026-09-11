namespace TheLithium.Imprint.Reporting;

internal static class SnapshotStatusText
{
    internal static string Format(SnapshotStatus value) => value switch
    {
        SnapshotStatus.Matched => "Matched",
        SnapshotStatus.Missing => "Missing",
        SnapshotStatus.Changed => "Changed",
        SnapshotStatus.Unused => "Unused",
        SnapshotStatus.Created => "Created",
        SnapshotStatus.Updated => "Updated",
        SnapshotStatus.Removed => "Removed",
        _ => "Unknown"
    };
}
