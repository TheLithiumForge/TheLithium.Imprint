namespace TheLithium.Imprint.Serialization;

/// <summary>Stops serialization before its output buffer can exceed the configured byte budget.</summary>
internal sealed class SizeLimitedStream(int limit) : MemoryStream
{
    public override void Write(byte[] buffer, int offset, int count)
    {
        Check(count);
        base.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Check(buffer.Length);
        base.Write(buffer);
    }

    public override void WriteByte(byte value)
    {
        Check(1);
        base.WriteByte(value);
    }

    private void Check(int additional)
    {
        if (Position + additional > limit)
        {
            throw new SnapshotCaptureException($"Snapshot exceeds its {limit}-byte limit.");
        }
    }
}
