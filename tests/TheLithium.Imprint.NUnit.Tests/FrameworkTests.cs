using NUnit.Framework;

namespace TheLithium.Imprint.NUnit.Tests;

public sealed class FrameworkTests
{
    [Test(Description = "NUnit creates readable snapshots")]
    public void Description() => Snapshots.Run(() =>
    {
        Assert.That(Snapshots.Current.BaselineDirectory, Does.EndWith("NUnit creates readable snapshots"));
        new { Framework = "NUnit", Supported = true }.Snapshot("result");
        "NUnit output".Snapshot("output");
    });

    [Test]
    public Task AsyncTest() => Snapshots.RunAsync(async () =>
    {
        await Task.Yield();
        42.Snapshot("answer");
    });

    [TestCase("small", 1)]
    [TestCase("large", 100)]
    public void Parameterized(string caseName, int count) => Snapshots.Run(() =>
    {
        new { Count = count }.Snapshot("result");
    }, new() { Case = caseName });
}
