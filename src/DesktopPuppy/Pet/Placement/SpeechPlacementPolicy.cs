using DesktopPuppy.Native;

namespace DesktopPuppy.Pet.Placement;

public static class SpeechPlacementPolicy
{
    public static PhysicalRect Place(PhysicalRect pet, int width, int height, PhysicalRect work, int gap)
    {
        width = Math.Min(width, work.Width);
        height = Math.Min(height, work.Height);
        var left = Math.Clamp(pet.Left + (pet.Width - width) / 2, work.Left, work.Right - width);
        var top = pet.Top - gap - height;
        if (top < work.Top) top = pet.Bottom + gap;
        top = Math.Clamp(top, work.Top, work.Bottom - height);
        return new PhysicalRect(left, top, left + width, top + height);
    }
}
