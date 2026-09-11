using NUnit.Framework;

namespace TheLithium.Imprint.NUnit.Tests;

public sealed class FrameworkTests
{
    [Test(Description = "NUnit creates readable snapshots")]
    public void Description()
    {
        Assert.That(Snapshots.Current.BaselineDirectory, Does.EndWith(nameof(Description)));
        new
        {
            Framework = "NUnit",
            Supported = true
        }.AssertSnapshot("result");
        "NUnit output".AssertSnapshot("output");
    }

    [Test]
    public async Task AsyncTest()
    {
        await Task.Yield();
        42.AssertSnapshot("answer");
    }

    [TestCase("small", 1)]
    [TestCase("large", 100)]
    public void Parameterized(string caseName, int count)
    {
        new
        {
            Count = count
        }.AssertSnapshot("result");
    }
}
