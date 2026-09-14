namespace TheLithium.Imprint.Storage;

/// <summary>Reads strict UTF-8 while bounding allocation even if a file grows after it is opened.</summary>
internal static class SnapshotFileReader
{
    internal static string ReadText(string path, int maximum, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            return ReadText(input, maximum, cancellation);
        }
        catch (SnapshotCaptureException error)
        {
            throw new SnapshotException($"File exceeds its {maximum}-byte limit: {path}", error);
        }
    }

    internal static string ReadText(Stream input, int maximum, CancellationToken cancellation = default)
    {
        using var output = new SizeLimitedStream(maximum);
        Span<byte> chunk = stackalloc byte[8192];
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            // Read at most one excess byte to distinguish an exact-size file from overflow.
            var count = input.Read(chunk[..(int)Math.Min(chunk.Length, maximum - output.Length + 1)]);
            if (count == 0)
            {
                break;
            }
            output.Write(chunk[..count]);
        }
        cancellation.ThrowIfCancellationRequested();
        return SnapshotEncoding.Utf8.GetString(output.ToArray());
    }
}
