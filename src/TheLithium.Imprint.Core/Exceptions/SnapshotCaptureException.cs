namespace TheLithium.Imprint;

/// <summary>A value could not be captured safely or a duplicate name was used.</summary>
public sealed class SnapshotCaptureException : SnapshotException
{
    public SnapshotCaptureException(string message) : base(message) { }
    public SnapshotCaptureException(string message, Exception inner) : base(message, inner) { }
}
