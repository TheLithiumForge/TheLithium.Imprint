namespace TheLithium.Imprint;

/// <summary>Selects the representation written to a snapshot file.</summary>
public enum SnapshotFormat
{
    /// <summary>Use text for strings and canonical JSON for other values.</summary>
    Auto = 0,
    /// <summary>JSON representation. Strings are values unless StringContent explicitly selects supplied JSON.</summary>
    Json = 1,
    /// <summary>Exact UTF-8 text with a .txt extension. Requires a non-null string.</summary>
    Text = 2
}
