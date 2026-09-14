using TheLithium.Imprint;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExternalMSTest;

[TestClass]
[SnapshotSettings(Name = "Review suite", Update = SnapshotUpdate.Missing)]
public sealed class SnapshotTests
{
    [TestMethod]
    public void PlainObject() => new Payload(7, Mode.Ready).AssertSnapshot("value");

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void Parameterized(int row) => new Payload(row, Mode.Ready).AssertSnapshot("row");

    [TestMethod]
    public async Task AsyncConfiguration()
    {
        Snapshots.Current.Representation = new() { Enums = SnapshotEnumRepresentation.Number };
        await Task.Yield();
        new Payload(8, Mode.Ready).AssertSnapshot("value");
    }

    [TestMethod]
    [SnapshotSettings(Name = "Indirect")]
    public void DelegateCapture()
    {
        Action capture = () => new Payload(9, Mode.Ready).AssertSnapshot("value");
        capture();
    }
}

public enum Mode
{
    None, Ready
}
public sealed record Payload(int Id, Mode Mode);

