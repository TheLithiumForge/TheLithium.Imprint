namespace TheLithium.Imprint.Configuration;

internal static class SnapshotFileNames
{
    internal const string JsonExtension = ".json";
    internal const string TextExtension = ".txt";
    internal const string SnapExtension = ".snap";

    internal static string Extension(SnapshotFormat format) => format switch
    {
        SnapshotFormat.Json => JsonExtension,
        SnapshotFormat.Text => TextExtension,
        SnapshotFormat.Snap => SnapExtension,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "A snapshot file requires a resolved format.")
    };

    internal static bool IsSnapshotFile(string file)
    {
        var extension = Path.GetExtension(file);
        return extension.Equals(JsonExtension, StringComparison.OrdinalIgnoreCase)
            || extension.Equals(TextExtension, StringComparison.OrdinalIgnoreCase)
            || extension.Equals(SnapExtension, StringComparison.OrdinalIgnoreCase);
    }
}
