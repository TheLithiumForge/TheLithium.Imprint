namespace TheLithium.Imprint.Configuration;

/// <summary>What a run does with snapshot folders that no current test owns.</summary>
internal enum SnapshotPrune
{
    /// <summary>Remove stale folders in every layer that ran completely. A run-wide Verify only reports them.</summary>
    Auto = 0,
    /// <summary>Report stale folders without removing them.</summary>
    Report = 1,
    /// <summary>Neither look for nor report stale folders.</summary>
    Off = 2
}
