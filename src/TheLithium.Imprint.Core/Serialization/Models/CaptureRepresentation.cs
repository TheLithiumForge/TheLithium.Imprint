namespace TheLithium.Imprint.Serialization.Models;

internal sealed record CaptureRepresentation(
    SnapshotFormat Format, SnapshotStringContent StringContent, ResolvedSnapshotRepresentation Options);
