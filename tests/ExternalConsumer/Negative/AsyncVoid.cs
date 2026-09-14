using TheLithium.Imprint;

public static class AsyncVoid
{
    public static async void Capture()
    {
        await Task.Yield();
        1.AssertSnapshot("value");
    }
}
