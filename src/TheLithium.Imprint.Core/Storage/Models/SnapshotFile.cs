namespace TheLithium.Imprint.Storage.Models;

/// <summary>
/// One snapshot file as stored bytes. Text formats also carry their strict UTF-8 text, which round-trips to the
/// same bytes, so text and binary files share one fingerprint, journal and write path.
/// </summary>
internal sealed class SnapshotFile
{
    private readonly string? _text;

    private SnapshotFile(byte[] content, string? text)
    {
        Content = content;
        _text = text;
    }

    internal byte[] Content
    {
        get;
    }

    internal bool IsText => _text is not null;

    /// <summary>The decoded text of a text snapshot. Binary snapshots have none.</summary>
    internal string Text => _text ?? throw new InvalidOperationException("A binary snapshot has no text.");

    internal static SnapshotFile FromText(string text) => new(SnapshotEncoding.Utf8.GetBytes(text), text);

    internal static SnapshotFile FromBytes(byte[] content) => new(content, null);

    /// <summary>Decodes text formats strictly, so an invalid UTF-8 baseline fails when it's read.</summary>
    internal static SnapshotFile Read(string fileName, byte[] content)
        => SnapshotFileNames.IsText(fileName) ? new(content, SnapshotEncoding.Utf8.GetString(content)) : new(content, null);

    internal bool SameContent(SnapshotFile other) => Content.AsSpan().SequenceEqual(other.Content);
}
