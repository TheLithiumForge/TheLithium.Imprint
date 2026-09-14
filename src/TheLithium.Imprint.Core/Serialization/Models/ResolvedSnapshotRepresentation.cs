namespace TheLithium.Imprint;

/// <summary>Concrete representation preferences received by writers after configuration layers are merged.</summary>
public sealed record ResolvedSnapshotRepresentation
{
    internal ResolvedSnapshotRepresentation()
    {
    }

    /// <summary>Enum names with numeric fallback, or always the underlying number.</summary>
    public SnapshotEnumRepresentation Enums { get; internal init; } = SnapshotEnumRepresentation.NameOrNumber;
    /// <summary>Numeric byte arrays or a base64 JSON string.</summary>
    public SnapshotByteArrayRepresentation ByteArrays { get; internal init; } = SnapshotByteArrayRepresentation.Numbers;
    /// <summary>Automatic dictionary shape or explicit Key/Value entry arrays.</summary>
    public SnapshotDictionaryRepresentation Dictionaries { get; internal init; } = SnapshotDictionaryRepresentation.Automatic;

    internal static ResolvedSnapshotRepresentation Defaults { get; } = new();

    internal ResolvedSnapshotRepresentation Apply(SnapshotRepresentationOptions? patch)
    {
        if (patch is null)
        {
            return this;
        }
        patch.Validate();
        return this with
        {
            Enums = patch.Enums ?? Enums,
            ByteArrays = patch.ByteArrays ?? ByteArrays,
            Dictionaries = patch.Dictionaries ?? Dictionaries
        };
    }

    internal SnapshotRepresentationOptions ToOptions() => new()
    {
        Enums = Enums,
        ByteArrays = ByteArrays,
        Dictionaries = Dictionaries
    };
}
