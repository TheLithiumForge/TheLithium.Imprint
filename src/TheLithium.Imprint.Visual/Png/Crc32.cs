namespace TheLithium.Imprint.Visual.Png;

/// <summary>The CRC-32 that PNG chunks use: polynomial 0xEDB88320, reflected, with a final inversion.</summary>
internal static class Crc32
{
    private static readonly uint[] Table = Build();

    internal static uint Compute(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
        => ~Update(Update(uint.MaxValue, first), second);

    private static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }
        return crc;
    }

    private static uint[] Build()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            }
            table[index] = value;
        }
        return table;
    }
}
