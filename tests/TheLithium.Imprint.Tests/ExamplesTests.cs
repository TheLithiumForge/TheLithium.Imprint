using Xunit;

namespace TheLithium.Imprint.Tests;

public sealed class ExamplesTests
{
    [Fact(DisplayName = "An order has structured data and readable output")]
    public void Order()
    {
        var order = new
        {
            Id = "order-123",
            Total = 42.50m,
            Items = new[] { "Keyboard", "Cable" }
        };
        order.AssertSnapshot();
        order.Items.AssertSnapshot("items");
        "Order created\nReady for dispatch\n".AssertSnapshot("output");
        Assert.EndsWith(Path.Combine(nameof(ExamplesTests), nameof(Order)), Snapshots.Current.BaselineDirectory);
        Assert.Equal(42.50m, order.Total);
    }

    [Fact]
    public async Task AsyncTest()
    {
        await Task.Yield();
        var state = new
        {
            Status = "complete",
            Count = 2
        };
        state.AssertSnapshot();
    }

    [Theory]
    [InlineData("first", 1)]
    [InlineData("second", 2)]
    public void Parameterized(string caseName, int count)
    {
        _ = caseName;
        new
        {
            Count = count
        }.AssertSnapshot("result");
    }

    [Fact(DisplayName = "Framework name")]
    [SnapshotSettings(Name = "Explicit snapshot name")]
    public void ExplicitNameWins()
    {
        Assert.EndsWith("Explicit snapshot name", Snapshots.Current.BaselineDirectory);
        true.AssertSnapshot("value");
    }
}
