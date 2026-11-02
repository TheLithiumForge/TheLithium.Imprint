using System.Runtime.InteropServices;

namespace TheLithium.Imprint.Visual;

/// <summary>One pixel as straight, not premultiplied, 8-bit red, green, blue and alpha, in that byte order.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Rgba32(byte R, byte G, byte B, byte A)
{
    /// <summary>An opaque pixel.</summary>
    public static Rgba32 Opaque(byte r, byte g, byte b) => new(r, g, b, 255);
}
