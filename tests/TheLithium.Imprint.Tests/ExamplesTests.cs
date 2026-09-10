using Xunit;

namespace TheLithium.Imprint.Tests;

public sealed class ExamplesTests
{
    [Fact(DisplayName = "An order has structured data and readable output")]
    public void Order() => Snapshots.Run(() =>
    {
        var order = new { Id = "order-123", Total = 42.50m, Items = new[] { "Keyboard", "Cable" } };
        order.Snapshot();
        order.Items.Snapshot("items");
        "Order created\nReady for dispatch\n".Snapshot("output");
        Assert.EndsWith(Path.Combine(nameof(ExamplesTests), "An order has structured data and readable output"), Snapshots.Current.BaselineDirectory);
        Assert.Equal(42.50m, order.Total);
    });

    [Fact]
    public Task AsyncTest() => Snapshots.RunAsync(async () =>
    {
        await Task.Yield();
        var state = new { Status = "complete", Count = 2 };
        state.Snapshot();
    });

    [Theory]
    [InlineData("first", 1)]
    [InlineData("second", 2)]
    public void Parameterized(string caseName, int count) => Snapshots.Run(() =>
    {
        new { Count = count }.Snapshot("result");
    }, new() { Case = caseName });

    [Fact(DisplayName = "Framework name")]
    [SnapshotSettings(Name = "Explicit snapshot name")]
    public void ExplicitNameWins() => Snapshots.Run(() =>
    {
        Assert.EndsWith("Explicit snapshot name", Snapshots.Current.BaselineDirectory);
        true.Snapshot("value");
    });
}
