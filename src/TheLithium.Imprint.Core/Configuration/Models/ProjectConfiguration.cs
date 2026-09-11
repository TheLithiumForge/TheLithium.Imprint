namespace TheLithium.Imprint.Configuration.Models;

internal sealed record ProjectConfiguration
{
    internal SnapshotUpdate Update { get; init; } = SnapshotUpdate.Missing;
    internal string DirectoryName { get; init; } = "__snapshots__";
    internal bool PreferDisplayNames
    {
        get; init;
    }
    internal string? RootDirectory
    {
        get; init;
    }
    internal string? ArtifactDirectory
    {
        get; init;
    }
    internal SnapshotFormat TextFormat { get; init; } = SnapshotFormat.Text;
    internal SnapshotNaming Naming { get; init; } = SnapshotNaming.NameThenOrder;
    internal SnapshotComparison Comparison { get; init; } = new();
    internal bool AllowEmpty
    {
        get; init;
    }
    internal int MaxDepth { get; init; } = SnapshotLimits.DefaultDepth;
    internal int MaxNodes { get; init; } = SnapshotLimits.DefaultNodes;
    internal int MaxBytes { get; init; } = SnapshotLimits.DefaultBytes;
    internal int LockTimeoutSeconds { get; init; } = 10;
}
