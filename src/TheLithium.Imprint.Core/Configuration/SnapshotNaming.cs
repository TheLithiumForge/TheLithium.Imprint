namespace TheLithium.Imprint;

/// <summary>Chooses a filename when a capture does not supply a name.</summary>
public enum SnapshotNaming
{
    /// <summary>Infer a variable or member name, then fall back to snapshot-1, snapshot-2, and so on.</summary>
    NameThenOrder = 0,
    /// <summary>Always use capture order: snapshot-1, snapshot-2, and so on.</summary>
    Order = 1,
    /// <summary>Require an explicit name at every capture.</summary>
    ExplicitOnly = 2
}
