namespace TheLithium.Imprint.Configuration;

internal static class SnapshotProtocol
{
    internal const string Version = "TheLithium.Imprint/1";
    internal const string JournalMarker = Version + "\n";

    /// <summary>
    /// Marks a journal that holds a binary snapshot. Versions that know only text reject it instead of restoring its
    /// text files and leaving a half-updated image behind.
    /// </summary>
    internal const string BinaryJournalMarker = "TheLithium.Imprint/2\n";
}
