namespace TheLithium.Imprint;

/// <summary>Concrete equality rules received by comparers. Every field has a value; omitted fields use built-in defaults.</summary>
public sealed record ResolvedSnapshotComparison
{
    /// <summary>Maximum absolute decimal difference accepted between JSON numbers.</summary>
    public decimal NumericTolerance
    {
        get; init;
    }
    /// <summary>Match JSON array elements without order, preserving duplicate counts.</summary>
    public bool IgnoreArrayOrder
    {
        get; init;
    }
    /// <summary>Compare string values with ordinal case-insensitive equality.</summary>
    public bool IgnoreStringCase
    {
        get; init;
    }
    /// <summary>Treat CRLF and LF as equal in text snapshots.</summary>
    public bool IgnoreLineEndings { get; init; } = true;
    /// <summary>Ignore trailing spaces and tabs on each text line.</summary>
    public bool IgnoreTrailingWhitespace
    {
        get; init;
    }
    /// <summary>Maximum array length accepted for unordered matching, from 1 through 1,024.</summary>
    public int MaxUnorderedArrayLength { get; init; } = 256;

    internal static ResolvedSnapshotComparison Defaults { get; } = new();

    internal ResolvedSnapshotComparison Apply(SnapshotComparison? patch)
    {
        if (patch is null)
        {
            return this;
        }
        Settings.ValidateComparison(patch);
        return this with
        {
            NumericTolerance = patch.NumericTolerance ?? NumericTolerance,
            IgnoreArrayOrder = patch.IgnoreArrayOrder ?? IgnoreArrayOrder,
            IgnoreStringCase = patch.IgnoreStringCase ?? IgnoreStringCase,
            IgnoreLineEndings = patch.IgnoreLineEndings ?? IgnoreLineEndings,
            IgnoreTrailingWhitespace = patch.IgnoreTrailingWhitespace ?? IgnoreTrailingWhitespace,
            MaxUnorderedArrayLength = patch.MaxUnorderedArrayLength ?? MaxUnorderedArrayLength
        };
    }

    internal SnapshotComparison ToOptions() => new()
    {
        NumericTolerance = NumericTolerance,
        IgnoreArrayOrder = IgnoreArrayOrder,
        IgnoreStringCase = IgnoreStringCase,
        IgnoreLineEndings = IgnoreLineEndings,
        IgnoreTrailingWhitespace = IgnoreTrailingWhitespace,
        MaxUnorderedArrayLength = MaxUnorderedArrayLength
    };
}
