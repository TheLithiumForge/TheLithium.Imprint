namespace TheLithium.Imprint.Execution.Models;

/// <summary>What a format package stages through <see cref="SnapshotScope.CaptureBinary"/>.</summary>
internal sealed record BinaryCapture
{
    /// <summary>The explicit name, or null to infer it from <see cref="Expression"/> like AssertSnapshot does.</summary>
    public required string? Name
    {
        get; init;
    }

    public required string? Expression
    {
        get; init;
    }

    /// <summary>A binary snapshot extension that <see cref="SnapshotFileNames"/> knows, e.g. <c>.png</c>.</summary>
    public required string Extension
    {
        get; init;
    }

    public required byte[] Content
    {
        get; init;
    }

    public required SnapshotUpdate Update
    {
        get; init;
    }

    public required IBinarySnapshotComparer Comparer
    {
        get; init;
    }
}
