namespace TheLithium.Imprint.Configuration;

internal sealed record EffectiveSettings
{
    public required SnapshotTestIdentity Identity
    {
        get; init;
    }
    public required string IdentityKey
    {
        get; init;
    }
    public required string BaselineRoot
    {
        get; init;
    }
    public required string TestDirectory
    {
        get; init;
    }
    public required string ArtifactRoot
    {
        get; init;
    }
    public required string StorageRoot
    {
        get; init;
    }
    public required SnapshotUpdate Update
    {
        get; init;
    }
    public required SnapshotUpdate? RunOverride
    {
        get; init;
    }
    public required bool ReadOnly
    {
        get; init;
    }
    public required SnapshotComparison Comparison
    {
        get; init;
    }
    public required SnapshotNaming Naming
    {
        get; init;
    }
    public required SnapshotFormat TextFormat
    {
        get; init;
    }
    public required bool AllowEmpty
    {
        get; init;
    }
    public required int MaxDepth
    {
        get; init;
    }
    public required int MaxNodes
    {
        get; init;
    }
    public required int MaxBytes
    {
        get; init;
    }
    public required TimeSpan LockTimeout
    {
        get; init;
    }
    public required CancellationToken Cancellation
    {
        get; init;
    }

    internal string DisplayName => TestNames.DisplayName(Identity);

    internal SnapshotUpdate ResolveUpdate(SnapshotUpdate entry, SnapshotUpdate test)
    {
        if (ReadOnly)
        {
            return SnapshotUpdate.Verify;
        }
        if (RunOverride is { } run)
        {
            return run;
        }
        return entry != SnapshotUpdate.Inherit ? entry : test;
    }
}
