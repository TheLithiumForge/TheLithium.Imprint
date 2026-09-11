namespace TheLithium.Imprint;

/// <summary>A value could not be captured safely or a duplicate name was used.</summary>
public sealed class SnapshotCaptureException : SnapshotException
{
    /// <summary>Creates a capture failure.</summary>
    public SnapshotCaptureException(string message) : base(message) { }
    /// <summary>Creates a capture failure with its cause.</summary>
    public SnapshotCaptureException(string message, Exception inner) : base(message, inner) { }
}
