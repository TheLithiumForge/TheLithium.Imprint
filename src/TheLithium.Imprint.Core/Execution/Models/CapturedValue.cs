namespace TheLithium.Imprint.Execution.Models;

internal sealed record CapturedValue(string Name, string FileName, string Text,
    SnapshotFormat Format, SnapshotUpdate Update, SnapshotComparison Comparison, ISnapshotComparer? Comparer);
