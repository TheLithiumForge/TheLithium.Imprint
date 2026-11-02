namespace TheLithium.Imprint.Configuration;

internal static class SnapshotFileNames
{
    internal const string JsonExtension = ".json";
    internal const string TextExtension = ".txt";
    internal const string PngExtension = ".png";

    internal static string Extension(SnapshotFormat format) => format switch
    {
        SnapshotFormat.Json => JsonExtension,
        SnapshotFormat.Text => TextExtension,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "A snapshot file requires a resolved format.")
    };

    internal static bool IsSnapshotFile(string file) => IsText(file) || IsBinary(file);

    /// <summary>Text snapshots are read as strict UTF-8 and compared as text.</summary>
    internal static bool IsText(string file)
    {
        var extension = Path.GetExtension(file);
        return extension.Equals(JsonExtension, StringComparison.OrdinalIgnoreCase)
            || extension.Equals(TextExtension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Binary snapshots are kept as bytes and compared by the comparer that captured them.</summary>
    internal static bool IsBinary(string file) => Path.GetExtension(file).Equals(PngExtension, StringComparison.OrdinalIgnoreCase);
}
