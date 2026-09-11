using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace TheLithium.Imprint.MSTest.Tests;

[TestClass]
public sealed class FrameworkTests
{
    [TestMethod(DisplayName = "MSTest explicit display name")]
    public void DisplayName()
    {
        Assert.IsTrue(Snapshots.Current.BaselineDirectory.EndsWith(nameof(DisplayName), StringComparison.Ordinal));
        true.AssertSnapshot("supported");
    }

    [TestMethod]
    [Description("MSTest creates readable snapshots")]
    public void Description()
    {
        Assert.IsTrue(Snapshots.Current.BaselineDirectory.EndsWith(nameof(Description), StringComparison.Ordinal));
        new
        {
            Framework = "MSTest",
            Supported = true
        }.AssertSnapshot("result");
        "MSTest output".AssertSnapshot("output");
    }

    [TestMethod]
    public async Task AsyncTest()
    {
        await Task.Yield();
        42.AssertSnapshot("answer");
    }

    [TestMethod]
    [DataRow("small", 1)]
    [DataRow("large", 100)]
    public void Parameterized(string caseName, int count)
    {
        new
        {
            Count = count
        }.AssertSnapshot("result");
    }
}
