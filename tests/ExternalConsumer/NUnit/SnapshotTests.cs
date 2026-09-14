using TheLithium.Imprint;
using NUnit.Framework;

namespace ExternalNUnit;

[TestFixture]
[SnapshotSettings(Name = "Review suite", Update = SnapshotUpdate.Missing)]
public sealed class SnapshotTests
{
    [Test]
    public void PlainObject() => new Payload(7, Mode.Ready).AssertSnapshot("value");

    [TestCase(1)]
    [TestCase(2)]
    public void Parameterized(int row) => new Payload(row, Mode.Ready).AssertSnapshot("row");

    [Test]
    public async Task AsyncConfiguration()
    {
        Snapshots.Current.Representation = new() { Enums = SnapshotEnumRepresentation.Number };
        await Task.Yield();
        new Payload(8, Mode.Ready).AssertSnapshot("value");
    }

    [Test]
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

