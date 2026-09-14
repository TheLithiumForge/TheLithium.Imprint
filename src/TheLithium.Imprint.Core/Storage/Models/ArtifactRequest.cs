namespace TheLithium.Imprint.Storage.Models;

internal sealed record ArtifactRequest
{
    public required EffectiveSettings Settings
    {
        get; init;
    }
    public required string ExecutionId
    {
        get; init;
    }
    public required IReadOnlyList<CapturedValue> Values
    {
        get; init;
    }
    public required BaselineState Baseline
    {
        get; init;
    }
    public required IReadOnlyList<SnapshotEntryResult> Entries
    {
        get; init;
    }
    public bool Incomplete
    {
        get; init;
    }
    public string? Error
    {
        get; init;
    }
}
