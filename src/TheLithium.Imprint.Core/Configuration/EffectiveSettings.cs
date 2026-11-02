namespace TheLithium.Imprint.Configuration;

internal sealed record EffectiveSettings
{
    /// <summary>The generated descriptor the scope resolved, or null for a Core-only integration.</summary>
    public required SnapshotDescriptor? Descriptor
    {
        get; init;
    }
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
    /// <summary>Forbids baseline mutation and prepared recovery; locks and failure diagnostics may still write.</summary>
    public required bool ReadOnly
    {
        get; init;
    }
    public required ResolvedSnapshotComparison Comparison
    {
        get; init;
    }
    public required SnapshotNaming Naming
    {
        get; init;
    }
    public required ResolvedSnapshotRepresentation Representation
    {
        get; init;
    }
    public required SnapshotStringContent StringContent
    {
        get; init;
    }
    public required bool AllowEmpty
    {
        get; init;
    }
    public required int MaxNestingDepth
    {
        get; init;
    }
    public required int MaxValuesPerSnapshot
    {
        get; init;
    }
    public required int MaxBytesPerSnapshot
    {
        get; init;
    }
    /// <summary>The per-file limit for binary snapshots such as PNG, from <c>visual.maxBytesPerImage</c>.</summary>
    public required int MaxBytesPerImage
    {
        get; init;
    }
    /// <summary>The project's <c>visual</c> group, for the visual packages.</summary>
    public required VisualSettings Visual
    {
        get; init;
    }

    /// <summary>The combined byte budget of every snapshot in one test, read or captured.</summary>
    internal long TestBudget => Math.Max(Math.Max(MaxBytesPerSnapshot, MaxBytesPerImage), SnapshotLimits.TestBytes);

    /// <summary>The per-file limit for a stored snapshot of this file name.</summary>
    internal int MaxBytesFor(string fileName) => SnapshotFileNames.IsBinary(fileName) ? MaxBytesPerImage : MaxBytesPerSnapshot;
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
