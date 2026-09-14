namespace TheLithium.Imprint;

/// <summary>Execution settings for an explicit runner integration. Ordinary tests use attributes and snapshots.config.json.</summary>
public sealed record SnapshotTestOptions
{
    /// <summary>Overrides the generated method, suite, and project update policy.</summary>
    public SnapshotUpdate Update { get; init; } = SnapshotUpdate.Inherit;
    /// <summary>Optional stable test folder name.</summary>
    public string? Name
    {
        get; init;
    }
    /// <summary>Optional stable suite folder name.</summary>
    public string? Suite
    {
        get; init;
    }
    /// <summary>Stable parameterized case key. Normal package integration derives this from parameter values.</summary>
    public string? Case
    {
        get; init;
    }
    /// <summary>Additional target or configuration key when baselines intentionally differ.</summary>
    public string? Variant
    {
        get; init;
    }
    /// <summary>Bypasses generated source identity for a custom runner.</summary>
    public SnapshotTestIdentity? Identity
    {
        get; init;
    }
    /// <summary>Overrides the snapshots folder. Relative paths are project-relative; the source path, suite, and method are appended.</summary>
    public string? RootDirectory
    {
        get; init;
    }
    /// <summary>Directory for received files and failure reports. Must be outside the snapshots folder.</summary>
    public string? ArtifactDirectory
    {
        get; init;
    }
    /// <summary>Optional configuration file path. An explicitly requested file must exist.</summary>
    public string? ConfigurationFile
    {
        get; init;
    }
    /// <summary>Interpret root strings as values or already serialized JSON documents.</summary>
    public SnapshotStringContent? StringContent
    {
        get; init;
    }
    /// <summary>Overrides project representation preferences for this test.</summary>
    public SnapshotRepresentationOptions? Representation
    {
        get; init;
    }
    /// <summary>Overrides individual project comparison fields.</summary>
    public SnapshotComparison? Comparison
    {
        get; init;
    }
    /// <summary>Overrides automatic snapshot naming for this test.</summary>
    public SnapshotNaming? Naming
    {
        get; init;
    }
    /// <summary>Allows a test that captures no values. An All update can then remove its existing entries.</summary>
    public bool? AllowEmpty
    {
        get; init;
    }
    /// <summary>Deepest object or array nesting that will be serialized, from 1 through 256.</summary>
    public int? MaxNestingDepth
    {
        get; init;
    }
    /// <summary>Most values one capture may visit, from 1 through 10,000,000.</summary>
    public int? MaxValuesPerSnapshot
    {
        get; init;
    }
    /// <summary>Largest single snapshot file in UTF-8 bytes, from 1 through 256 MiB.</summary>
    public int? MaxBytesPerSnapshot
    {
        get; init;
    }
    /// <summary>Cancels Imprint-owned capture, comparison, and lock waiting. Custom callbacks are not interrupted or passed the token. An already-started storage transaction completes or rolls back.</summary>
    public CancellationToken CancellationToken
    {
        get; init;
    }
}
