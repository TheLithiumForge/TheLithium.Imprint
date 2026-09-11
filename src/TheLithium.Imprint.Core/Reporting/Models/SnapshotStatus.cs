namespace TheLithium.Imprint;

/// <summary>The comparison or approval result for one snapshot entry.</summary>
public enum SnapshotStatus
{
    /// <summary>The baseline matches.</summary>
    Matched,
    /// <summary>No baseline exists.</summary>
    Missing,
    /// <summary>The baseline differs.</summary>
    Changed,
    /// <summary>An existing entry was not captured by this test run.</summary>
    Unused,
    /// <summary>A missing baseline was approved.</summary>
    Created,
    /// <summary>A changed baseline was approved.</summary>
    Updated,
    /// <summary>An unused baseline was removed by a test-wide All update.</summary>
    Removed
}
