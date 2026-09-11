namespace TheLithium.Imprint;

/// <summary>Base class for Imprint assertion, configuration, capture, and storage failures.</summary>
public class SnapshotException : Exception
{
    /// <summary>Creates an Imprint failure with a user-facing message.</summary>
    public SnapshotException(string message) : base(RequiredMessage(message)) { }
    /// <summary>Creates an Imprint failure with a user-facing message and cause.</summary>
    public SnapshotException(string message, Exception inner)
        : base(RequiredMessage(message), RequiredInner(inner)) { }

    private static string RequiredMessage(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message;
    }

    private static Exception RequiredInner(Exception inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return inner;
    }
}
