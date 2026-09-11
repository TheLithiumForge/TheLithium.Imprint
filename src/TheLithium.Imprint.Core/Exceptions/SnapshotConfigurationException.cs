namespace TheLithium.Imprint;

/// <summary>Invalid snapshot configuration or missing build integration.</summary>
public sealed class SnapshotConfigurationException : SnapshotException
{
    /// <summary>Creates a configuration failure.</summary>
    public SnapshotConfigurationException(string message) : base(message) { }
}
