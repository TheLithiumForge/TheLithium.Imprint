using Xunit;

namespace TheLithium.Imprint.Tests;

public sealed class NormalFlowTests
{
    [Fact, SnapshotSettings(Update = SnapshotUpdate.Missing)]
    public void CapturesMultipleValuesInAnOrdinaryTest()
    {
        var result = new { Name = "Lithium", Count = 2 };
        result.AssertSnapshot();
        Assert.Equal(2, result.Count);

        var message = "Ready\n";
        message.AssertSnapshot();
        Assert.EndsWith(nameof(CapturesMultipleValuesInAnOrdinaryTest), Snapshots.Current.BaselineDirectory);
    }

    [Fact, SnapshotSettings(Update = SnapshotUpdate.Missing)]
    public async Task CapturesAcrossAwait()
    {
        "before".AssertSnapshot("first");
        await Task.Yield();
        "after".AssertSnapshot("second");
    }

    [Theory, InlineData("first", 1), InlineData("second", 2), SnapshotSettings(Update = SnapshotUpdate.Missing)]
    public void UsesArgumentsToSeparateCases(string label, int count)
    {
        new { label, count }.AssertSnapshot("result");
    }

    [Fact]
    public async Task AwaitsAValueTaskTestBody() => await SupportsValueTask();

    [SnapshotSettings(Update = SnapshotUpdate.Missing)]
    public async ValueTask SupportsValueTask()
    {
        await Task.Yield();
        42.AssertSnapshot("answer");
    }
}
