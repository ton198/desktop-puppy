namespace DesktopPuppy.Native;

public readonly record struct PhysicalRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsValid => Right > Left && Bottom > Top;
}
