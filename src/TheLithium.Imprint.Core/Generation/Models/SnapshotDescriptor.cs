using System.ComponentModel;

namespace TheLithium.Imprint.Generation.Models;

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SnapshotDescriptor(string ProjectDirectory, string ProjectName,
    string SourceFile, string Suite, string Test, string LogicalId,
    SnapshotUpdate Update, bool RequiresCase, string? BuildArtifactsDirectory = null,
    string? DisplayName = null)
{
    /// <summary>Number of inline data rows, when they are the method's only data source.</summary>
    public int? CaseCount
    {
        get; init;
    }

    /// <summary>A helper called by another lifetime method, or a test its attributes skip.</summary>
    public bool Optional
    {
        get; init;
    }

    /// <summary>The method opens its own scope, so its snapshot names are decided at runtime.</summary>
    public bool Explicit
    {
        get; init;
    }

    /// <summary>Another build configuration or target framework may compile a different set of tests.</summary>
    public bool Conditional
    {
        get; init;
    }
}
