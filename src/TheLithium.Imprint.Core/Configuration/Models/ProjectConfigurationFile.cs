using System.Text.Json.Serialization;

namespace TheLithium.Imprint.Configuration.Models;

internal sealed class ProjectConfigurationFile
{
    [JsonPropertyName("$schema")]
    public string Schema { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public SnapshotUpdate Update { get; set; } = SnapshotUpdate.Missing;
    public bool AllowEmptyTests
    {
        get; set;
    }
    public SnapshotStringContent StringContent
    {
        get; set;
    }
    public FileConfiguration Files { get; set; } = new();
    public NamingConfiguration Naming { get; set; } = new();
    public ComparisonConfiguration Comparison { get; set; } = new();
    public RepresentationConfiguration Representation { get; set; } = new();
    public LimitsConfiguration Limits { get; set; } = new();
}

internal sealed class FileConfiguration
{
    public string SnapshotFolderName { get; set; } = "__snapshots__";
    public string? SnapshotRootPath
    {
        get; set;
    }
    public string? FailureArtifactPath
    {
        get; set;
    }
}

internal sealed class NamingConfiguration
{
    public SnapshotNaming UnnamedCaptures { get; set; } = SnapshotNaming.NameThenOrder;
    public bool UseFrameworkDisplayNames
    {
        get; set;
    }
}

internal sealed class ComparisonConfiguration
{
    public decimal NumericTolerance
    {
        get; set;
    }
    public bool IgnoreArrayOrder
    {
        get; set;
    }
    public bool IgnoreStringCase
    {
        get; set;
    }
    public bool IgnoreLineEndings { get; set; } = ResolvedSnapshotComparison.Defaults.IgnoreLineEndings;
    public bool IgnoreTrailingWhitespace
    {
        get; set;
    }
    public int MaxUnorderedArrayLength { get; set; } = ResolvedSnapshotComparison.Defaults.MaxUnorderedArrayLength;
}

internal sealed class RepresentationConfiguration
{
    public SnapshotEnumRepresentation Enums
    {
        get; set;
    }
    public SnapshotByteArrayRepresentation ByteArrays
    {
        get; set;
    }
    public SnapshotDictionaryRepresentation Dictionaries
    {
        get; set;
    }
}

internal sealed class LimitsConfiguration
{
    public int MaxNestingDepth { get; set; } = SnapshotLimits.DefaultDepth;
    public int MaxValuesPerSnapshot { get; set; } = SnapshotLimits.DefaultNodes;
    public int MaxBytesPerSnapshot { get; set; } = SnapshotLimits.DefaultBytes;
    public int LockTimeoutSeconds { get; set; } = 10;
}

