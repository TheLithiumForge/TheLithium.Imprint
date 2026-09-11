namespace TheLithium.Imprint;

/// <summary>Overlapping test identities or a concurrent change prevented safe approval.</summary>
public sealed class SnapshotConflictException : SnapshotException
{
    /// <summary>Creates a storage or identity conflict.</summary>
    public SnapshotConflictException(string message) : base(message) { }
}
