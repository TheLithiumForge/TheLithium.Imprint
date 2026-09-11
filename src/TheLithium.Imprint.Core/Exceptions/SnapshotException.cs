namespace TheLithium.Imprint;

/// <summary>Base class for Imprint assertion, configuration, capture, and storage failures.</summary>
public class SnapshotException : Exception
{
    public SnapshotException(string message) : base(message) { }
    public SnapshotException(string message, Exception inner) : base(message, inner) { }
}
