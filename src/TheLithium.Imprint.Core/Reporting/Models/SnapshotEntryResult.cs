namespace TheLithium.Imprint;

/// <summary>The result for a single named capture or unused baseline.</summary>
public sealed record SnapshotEntryResult
{
    /// <summary>Creates an entry result.</summary>
    /// <param name="Name">Portable snapshot name without its extension.</param>
    /// <param name="FileName">Filename including the resolved extension.</param>
    /// <param name="Status">Comparison or approval outcome.</param>
    /// <param name="Difference">Optional explanation or diff.</param>
    public SnapshotEntryResult(string Name, string FileName, SnapshotStatus Status, string? Difference = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(FileName);
        this.Name = Name;
        this.FileName = FileName;
        this.Status = Status;
        this.Difference = Difference;
    }

    /// <summary>Portable snapshot name without its extension.</summary>
    public string Name
    {
        get; init;
    }
    /// <summary>Filename including the resolved extension.</summary>
    public string FileName
    {
        get; init;
    }
    /// <summary>Comparison or approval outcome.</summary>
    public SnapshotStatus Status
    {
        get; init;
    }
    /// <summary>Optional explanation or diff.</summary>
    public string? Difference
    {
        get; init;
    }

    /// <summary>Deconstructs the entry into its result fields.</summary>
    public void Deconstruct(out string name, out string fileName, out SnapshotStatus status, out string? difference)
        => (name, fileName, status, difference) = (Name, FileName, Status, Difference);
}
