namespace TheLithium.Imprint;

/// <summary>Selects the representation written to a snapshot file.</summary>
public enum SnapshotFormat
{
    /// <summary>Use text for strings and canonical JSON for other values.</summary>
    Auto = 0,
    /// <summary>Canonical, indented JSON with sorted object properties. A string is parsed as JSON.</summary>
    Json = 1,
    /// <summary>Exact UTF-8 text with a .snap extension. Requires a non-null string.</summary>
    Snap = 2,
    /// <summary>Exact UTF-8 text with a .txt extension. Requires a non-null string.</summary>
    Text = 3
}
