using Xunit;

namespace TheLithium.Imprint.Tests;

public sealed class SnapshotWriteContextTests
{
    [Fact]
    public void ValueTypeFramesStillConsumeDepthAndRestoreItOnDisposal()
    {
        var context = new SnapshotWriteContext(maxDepth: 1, maxNodes: 10, CancellationToken.None);
        var frame = context.Enter(1);
        Assert.Contains("depth budget", Assert.Throws<SnapshotCaptureException>(() => context.Enter(2)).Message);
        frame.Dispose();
        frame.Dispose();
        using var next = context.Enter(3);
    }

    [Fact]
    public void NullableValuesIncludingNullConsumeNodeBudget()
    {
        var context = new SnapshotWriteContext(maxDepth: 10, maxNodes: 2, CancellationToken.None);
        using (context.Enter<int?>(null)) { }
        using (context.Enter<int?>(1)) { }
        Assert.Contains("node budget", Assert.Throws<SnapshotCaptureException>(() => context.Enter<int?>(null)).Message);
    }

    [Fact]
    public void ReferenceContractsTrackBoxIdentityAndReleaseItAfterDisposal()
    {
        var context = new SnapshotWriteContext(maxDepth: 10, maxNodes: 10, CancellationToken.None);
        object boxed = 42;
        using (context.Enter(boxed))
        {
            Assert.Contains("Reference cycle", Assert.Throws<SnapshotCaptureException>(() => context.Enter(boxed)).Message);
            using var differentBox = context.Enter((object)42);
        }
        using var sharedLater = context.Enter(boxed);
    }

    [Fact]
    public void ValueTypeEntryObservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new SnapshotWriteContext(maxDepth: 10, maxNodes: 10, cancellation.Token);
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => context.Enter(1));
    }
}
