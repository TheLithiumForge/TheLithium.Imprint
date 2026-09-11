namespace TheLithium.Imprint;

/// <summary>Controls which differences may be approved after a successful test body.</summary>
public enum SnapshotUpdate
{
    /// <summary>Use the containing test, suite, or project policy.</summary>
    Inherit = 0,
    /// <summary>Require every snapshot to exist and match; never write baselines.</summary>
    Verify = 1,
    /// <summary>Create missing snapshots. Existing differences still fail. This is the project default.</summary>
    Missing = 2,
    /// <summary>Create or replace snapshots, and remove unused entries when applied to the whole test.</summary>
    All = 3
}
