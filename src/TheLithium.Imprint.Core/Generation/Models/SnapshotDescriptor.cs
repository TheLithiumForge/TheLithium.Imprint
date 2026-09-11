using System.ComponentModel;

namespace TheLithium.Imprint.Generation.Models;

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SnapshotDescriptor(string ProjectDirectory, string ProjectName,
    string SourceFile, string Suite, string Test, string LogicalId,
    SnapshotUpdate Update, bool RequiresCase, string? BuildArtifactsDirectory = null,
    string? DisplayName = null);
