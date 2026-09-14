using TheLithium.Imprint;

public static class UnsupportedObject
{
    public static void Capture(object value) => value.AssertSnapshot("value");
}
