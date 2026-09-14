namespace TheLithium.Imprint.Serialization;

/// <summary>An append-only buffer whose contents and capacity cannot exceed its byte budget.</summary>
internal sealed class SizeLimitedStream(int limit) : Stream
{
    private readonly MemoryStream _buffer = new();

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => _buffer.CanWrite;
    public override long Length => _buffer.Length;
    public override long Position
    {
        get => _buffer.Position;
        set => throw new NotSupportedException("Snapshot output is append-only.");
    }

    internal byte[] ToArray() => _buffer.ToArray();

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var required = _buffer.Length + buffer.Length;
        if (required > limit)
        {
            throw new SnapshotCaptureException($"Snapshot exceeds its {limit}-byte limit.");
        }
        if (required > _buffer.Capacity)
        {
            _buffer.Capacity = (int)Math.Min(limit, Math.Max(required, Math.Max(256L, _buffer.Capacity * 2L)));
        }
        _buffer.Write(buffer);
    }

    public override void WriteByte(byte value)
    {
        Span<byte> single = stackalloc byte[1];
        single[0] = value;
        Write(single);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => _buffer.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException("Snapshot output is append-only.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _buffer.Dispose();
        }
        base.Dispose(disposing);
    }
}
