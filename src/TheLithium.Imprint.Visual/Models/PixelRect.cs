namespace TheLithium.Imprint.Visual;

/// <summary>A rectangle in physical pixels. It covers columns <c>X</c> to <c>X + Width - 1</c> and rows <c>Y</c> to <c>Y + Height - 1</c>.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    internal bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>True when the rectangle has an area and lies fully inside an image of this size.</summary>
    internal bool FitsIn(int width, int height)
        => !IsEmpty && X >= 0 && Y >= 0 && (long)X + Width <= width && (long)Y + Height <= height;

    internal static PixelRect Union(PixelRect first, int x, int y)
    {
        if (first.IsEmpty)
        {
            return new(x, y, 1, 1);
        }

        var left = Math.Min(first.X, x);
        var top = Math.Min(first.Y, y);
        var right = Math.Max(first.X + first.Width, x + 1);
        var bottom = Math.Max(first.Y + first.Height, y + 1);
        return new(left, top, right - left, bottom - top);
    }
}
