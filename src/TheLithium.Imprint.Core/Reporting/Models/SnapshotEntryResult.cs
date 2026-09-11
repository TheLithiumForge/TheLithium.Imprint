namespace TheLithium.Imprint;

/// <summary>The result for a single named capture or unused baseline.</summary>
/// <param name="Name">Portable snapshot name without its extension.</param>
/// <param name="FileName">Filename including the resolved extension.</param>
/// <param name="Status">Comparison or approval outcome.</param>
/// <param name="Difference">Optional explanation or diff.</param>
public sealed record SnapshotEntryResult(string Name, string FileName, SnapshotStatus Status, string? Difference = null);
