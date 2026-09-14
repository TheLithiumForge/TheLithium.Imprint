using System.Text;
using System.Text.Json;
using TheLithium.Imprint.Configuration;
using TheLithium.Imprint.Serialization;
using TheLithium.Imprint.Specifications;
using TheLithium.Imprint.Storage;
using Xunit;

namespace TheLithium.Imprint.Tests;

public sealed class IoBoundaryTests
{
    [Fact]
    public async Task AllOutputWritesRespectTheCap()
    {
        using var output = new SizeLimitedStream(5);
        output.WriteByte(1);
        output.Write([2], 0, 1);
        output.Write(new ReadOnlySpan<byte>([3]));
        await output.WriteAsync(new ReadOnlyMemory<byte>([4]));
        await output.WriteAsync([5], 0, 1, CancellationToken.None);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, output.ToArray());
        Assert.Throws<SnapshotCaptureException>(() => output.WriteByte(6));
        Assert.Throws<SnapshotCaptureException>(() => output.Write([6], 0, 1));
        Assert.Throws<SnapshotCaptureException>(() => output.Write(new ReadOnlySpan<byte>([6])));
        await Assert.ThrowsAsync<SnapshotCaptureException>(async () => await output.WriteAsync(new ReadOnlyMemory<byte>([6])));
        await Assert.ThrowsAsync<SnapshotCaptureException>(() => output.WriteAsync([6], 0, 1, CancellationToken.None));
        var operation = output.BeginWrite([6], 0, 1, null, null);
        Assert.Throws<SnapshotCaptureException>(() => output.EndWrite(operation));
        Assert.Equal(5, output.Length);
    }

    [Fact]
    public void OutputCannotBeResizedOrRepositioned()
    {
        using var output = new SizeLimitedStream(1);
        Assert.False(output.CanRead);
        Assert.False(output.CanSeek);
        Assert.Throws<NotSupportedException>(() => output.Position = 10);
        Assert.Throws<NotSupportedException>(() => output.Seek(10, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => output.SetLength(10));
        Assert.Throws<NotSupportedException>(() => output.ReadByte());
        output.WriteByte(42);
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        output.Dispose();
        Assert.False(output.CanWrite);
        Assert.Throws<ObjectDisposedException>(() => output.WriteByte(0));
    }

    [Fact]
    public async Task CancelledAsyncWriteDoesNotAppend()
    {
        using var output = new SizeLimitedStream(4);
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await output.WriteAsync(new ReadOnlyMemory<byte>([1]), new CancellationToken(true)));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void GrowingInputIsBoundedWithoutTrustingItsInitialLength()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "growing.txt");
        File.WriteAllText(path, "a");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var input = new GrowingFileStream(file, () => File.AppendAllText(path, new string('b', 100)));
        Assert.Throws<SnapshotCaptureException>(() => SnapshotFileReader.ReadText(input, 4));
        Assert.Equal(5, file.Position);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("abc", 3)]
    [InlineData("é", 2)]
    public void ExactUtf8BudgetIsAccepted(string text, int bytes)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(text));
        Assert.Equal(text, SnapshotFileReader.ReadText(input, bytes));
    }

    [Fact]
    public void InvalidUtf8IsRejected()
    {
        using var input = new MemoryStream([0xC3, 0x28]);
        Assert.Throws<DecoderFallbackException>(() => SnapshotFileReader.ReadText(input, 2));
    }

    [Theory]
    [InlineData("{\"nested\":{\"a\":1,\"a\":2}}")]
    [InlineData("{\"a\":1,\"\\u0061\":2}")]
    [InlineData("[1,]")]
    [InlineData("/*comment*/1")]
    [InlineData("1 2")]
    [InlineData("")]
    public void SharedParserRejectsInvalidDocuments(string json)
        => Assert.ThrowsAny<JsonException>(() => StrictJson.Parse(json, 16));

    [Theory]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("\"text\"")]
    [InlineData("[1,1]")]
    public void SharedParserAcceptsScalarsAndRepeatedArrayValues(string json)
    {
        using var document = StrictJson.Parse(json, 16);
        Assert.Equal(json, document.RootElement.GetRawText());
    }

    [Fact]
    public void ParserAndCanonicalizationHonorDepthAndCancellation()
    {
        Assert.ThrowsAny<JsonException>(() => StrictJson.Parse("[[1]]", 1));
        Assert.Throws<OperationCanceledException>(() => StrictJson.Parse("null", 1, new CancellationToken(true)));
        Assert.Throws<OperationCanceledException>(() => SnapshotEncoding.CanonicalJson("[1]", 4, 100, cancellation: new CancellationToken(true)));
        Assert.Throws<SnapshotCaptureException>(() => SnapshotEncoding.CanonicalJson("{\"a\":1}", 4, 100, maxNodes: 1));
        Assert.Equal("{\n  \"a\": 1\n}\n", SnapshotEncoding.CanonicalJson("{\"a\":1}", 4, 100, maxNodes: 2));
        Assert.Throws<SnapshotCaptureException>(() => SnapshotEncoding.CanonicalJson("1", 4, 1));
        Assert.Equal("1\n", SnapshotEncoding.CanonicalJson("1", 4, 2));
    }

    [Fact]
    public void ConfigurationReadIsBoundedAndKeepsUtf8BomSupport()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "snapshots.config.json");
        File.WriteAllText(path, "{}", new UTF8Encoding(true));
        ProjectConfigurationReader.Read(path, required: true);
        File.WriteAllText(path, new string(' ', 1024 * 1024 + 1));
        Assert.Throws<SnapshotConfigurationException>(() => ProjectConfigurationReader.Read(path, required: true));
        File.WriteAllBytes(path, [0xC3, 0x28]);
        Assert.Throws<SnapshotConfigurationException>(() => ProjectConfigurationReader.Read(path, required: true));
    }
}

/// <summary>Grows a real file after its first read without exposing a trustworthy length.</summary>
internal sealed class GrowingFileStream(Stream input, Action grow) : Stream
{
    private bool _grew;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException(); set => throw new NotSupportedException();
    }
    public override int Read(Span<byte> buffer)
    {
        var count = input.Read(buffer);
        if (!_grew)
        {
            _grew = true;
            grow();
        }
        return count;
    }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

