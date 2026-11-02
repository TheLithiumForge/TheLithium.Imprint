namespace TheLithium.Imprint.Execution.Models;

internal sealed record SnapshotDecision(SnapshotEntryResult Entry, SnapshotStatus ApprovedStatus,
    bool Authorized, CapturedValue? Capture = null, string? ExistingFile = null)
{
    /// <summary>Files a comparer produced to explain a difference, written with the failure artifacts.</summary>
    internal IReadOnlyList<SnapshotAttachment> Attachments { get; init; } = [];

    internal bool Changes => ApprovedStatus is SnapshotStatus.Created or SnapshotStatus.Updated or SnapshotStatus.Removed;

    internal SnapshotEntryResult ReportEntry(bool approved)
        => approved && Changes ? Entry with
        {
            Status = ApprovedStatus,
            Difference = null
        } : Entry;
}
