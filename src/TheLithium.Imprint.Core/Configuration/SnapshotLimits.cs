namespace TheLithium.Imprint.Configuration;

/// <summary>Shared storage and capture budgets validated by project configuration.</summary>
internal static class SnapshotLimits
{
    internal const int MaximumEntries = 1024;
    internal const int DefaultDepth = 64;
    internal const int MaximumDepth = 256;
    internal const int DefaultNodes = 100_000;
    internal const int MaximumNodes = 10_000_000;
    internal const int DefaultBytes = 4 * 1024 * 1024;
    internal const int MaximumBytes = 256 * 1024 * 1024;
    internal const int TestBytes = 128 * 1024 * 1024;
}
