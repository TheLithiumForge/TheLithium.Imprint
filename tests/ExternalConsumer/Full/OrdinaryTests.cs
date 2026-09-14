using System.ComponentModel;
using TheLithium.Imprint;

namespace ExternalConsumer;

[SnapshotSettings(Name = "Attributed suite", Update = SnapshotUpdate.All)]
public static class OrdinaryTests
{
    public static void ClassPolicy() => 12.AssertSnapshot("value");

    [SnapshotSettings(Name = "Method name", Update = SnapshotUpdate.Verify)]
    public static void MethodPolicy() => 13.AssertSnapshot("value");

    public static int ReturnsValue()
    {
        14.AssertSnapshot("value");
        return 14;
    }

    public static async Task ReturnsTask()
    {
        await Task.Yield();
        15.AssertSnapshot("value");
    }

    public static async Task<int> ReturnsTaskValue()
    {
        await Task.Yield();
        16.AssertSnapshot("value");
        return 16;
    }

    public static async ValueTask ReturnsValueTask()
    {
        await Task.Yield();
        17.AssertSnapshot("value");
    }

    public static async ValueTask<int> ReturnsValueTaskValue()
    {
        await Task.Yield();
        18.AssertSnapshot("value");
        return 18;
    }

    public static void Parameterized(int row) => row.AssertSnapshot("row");

    [Description("Readable test")]
    public static void DisplayName() => 19.AssertSnapshot("value");

    public static Action IndirectCapture { get; set; } = static () => { };

    [SnapshotSettings]
    public static void Indirect()
    {
        Snapshots.Current.Representation = new() { Enums = SnapshotEnumRepresentation.Number };
        IndirectCapture();
    }

    public static async Task FailingFinally()
    {
        try { 20.AssertSnapshot("value"); }
        finally
        {
            await Task.Yield();
            throw new InvalidOperationException("finally failed");
        }
    }
}
