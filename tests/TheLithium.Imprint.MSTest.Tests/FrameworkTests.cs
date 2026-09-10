using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace TheLithium.Imprint.MSTest.Tests;

[TestClass]
public sealed class FrameworkTests
{
    [TestMethod(DisplayName = "MSTest explicit display name")]
    public void DisplayName() => Snapshots.Run(() =>
    {
        Assert.IsTrue(Snapshots.Current.BaselineDirectory.EndsWith("MSTest explicit display name", StringComparison.Ordinal));
        true.Snapshot("supported");
    });

    [TestMethod]
    [Description("MSTest creates readable snapshots")]
    public void Description() => Snapshots.Run(() =>
    {
        Assert.IsTrue(Snapshots.Current.BaselineDirectory.EndsWith("MSTest creates readable snapshots", StringComparison.Ordinal));
        new { Framework = "MSTest", Supported = true }.Snapshot("result");
        "MSTest output".Snapshot("output");
    });

    [TestMethod]
    public async Task AsyncTest()
    {
        await Snapshots.RunAsync(async () =>
        {
            await Task.Yield();
            42.Snapshot("answer");
        });
    }

    [TestMethod]
    [DataRow("small", 1)]
    [DataRow("large", 100)]
    public void Parameterized(string caseName, int count) => Snapshots.Run(() =>
    {
        new { Count = count }.Snapshot("result");
    }, new() { Case = caseName });
}
