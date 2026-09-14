namespace TheLithium.Imprint;

/// <summary>Immutable representation preferences for a test or capture. Unset categories inherit from the broader scope.</summary>
public sealed record SnapshotRepresentationOptions
{
    /// <summary>Enum names with numeric fallback, or always the underlying number.</summary>
    public SnapshotEnumRepresentation? Enums
    {
        get; init;
    }
    /// <summary>Numeric byte arrays or a base64 JSON string.</summary>
    public SnapshotByteArrayRepresentation? ByteArrays
    {
        get; init;
    }
    /// <summary>Automatic dictionary shape or explicit Key/Value entry arrays.</summary>
    public SnapshotDictionaryRepresentation? Dictionaries
    {
        get; init;
    }

    internal void Validate()
    {
        if (Enums is { } enums && enums is not (SnapshotEnumRepresentation.NameOrNumber or SnapshotEnumRepresentation.Number)
            || ByteArrays is { } bytes && bytes is not (SnapshotByteArrayRepresentation.Numbers or SnapshotByteArrayRepresentation.Base64)
            || Dictionaries is { } dictionaries && dictionaries is not (SnapshotDictionaryRepresentation.Automatic or SnapshotDictionaryRepresentation.Entries))
        {
            throw new SnapshotConfigurationException("Invalid snapshot representation policy.");
        }
    }

}

/// <summary>Supported enum views.</summary>
public enum SnapshotEnumRepresentation
{
    NameOrNumber, Number
}
/// <summary>Supported byte array views; both are JSON values.</summary>
public enum SnapshotByteArrayRepresentation
{
    Numbers, Base64
}
/// <summary>Supported dictionary shapes.</summary>
public enum SnapshotDictionaryRepresentation
{
    Automatic, Entries
}
