namespace TheLithium.Imprint;

/// <summary>Per-field equality overrides. Unset fields inherit; equality never changes the saved representation.</summary>
public sealed record SnapshotComparison
{
    /// <summary>Maximum absolute decimal difference accepted between JSON numbers. Zero means exact numeric equality.</summary>
    public decimal? NumericTolerance
    {
        get; init;
    }
    /// <summary>Match JSON array elements without considering order; duplicates still count separately.</summary>
    public bool? IgnoreArrayOrder
    {
        get; init;
    }
    /// <summary>Compare string values using ordinal case-insensitive equality. Object property names remain case-sensitive.</summary>
    public bool? IgnoreStringCase
    {
        get; init;
    }
    /// <summary>Treat CRLF and LF as equal in text snapshots. Defaults to true; stored text remains unchanged.</summary>
    public bool? IgnoreLineEndings
    {
        get; init;
    }
    /// <summary>Ignore trailing spaces and tabs on each text line.</summary>
    public bool? IgnoreTrailingWhitespace
    {
        get; init;
    }
    /// <summary>Maximum length of an unordered array. Bounds the matching cost; valid range is 1 through 1024.</summary>
    public int? MaxUnorderedArrayLength
    {
        get; init;
    }
}
