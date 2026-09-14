namespace TheLithium.Imprint.Execution.Models;

internal sealed record SnapshotDecision(SnapshotEntryResult Entry, SnapshotStatus ApprovedStatus,
    bool Authorized, CapturedValue? Capture = null, string? ExistingFile = null)
{
    internal bool Changes => ApprovedStatus is SnapshotStatus.Created or SnapshotStatus.Updated or SnapshotStatus.Removed;

    internal SnapshotEntryResult ReportEntry(bool approved)
        => approved && Changes ? Entry with
        {
            Status = ApprovedStatus,
            Difference = null
        } : Entry;
}
