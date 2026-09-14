using TheLithium.Imprint;

public static class AsyncRun
{
    public static void Capture() => Snapshots.Run(async () =>
    {
        await Task.Yield();
        1.AssertSnapshot("value");
    });
}
