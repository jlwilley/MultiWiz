namespace MultiWiz.Core.Primitives;

/// <summary>A rectangle in physical screen pixels (virtual-desktop coordinates).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>A size in physical pixels.</summary>
public readonly record struct PixelSize(int Width, int Height);
