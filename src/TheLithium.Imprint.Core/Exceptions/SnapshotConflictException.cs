namespace TheLithium.Imprint;

/// <summary>Overlapping test identities or a concurrent change prevented safe approval.</summary>
public sealed class SnapshotConflictException : SnapshotException
{
    public SnapshotConflictException(string message) : base(message) { }
}
