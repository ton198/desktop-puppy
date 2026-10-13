namespace DesktopPuppy.Pet.Docking;

public static class EdgeRevealPose
{
    public static double LayoutBlend(double progress) => Math.Clamp(progress / .8, 0, 1);
    public static string Frame(DockSide side, double progress)
    {
        if (progress <= 0) return "idle";
        var suffix = side.ToString().ToLowerInvariant();
        if (progress >= .8) return $"edge-transition-{suffix}";
        return $"edge-reveal-{Math.Min(6, (int)(progress / .8 * 6) + 1)}-{suffix}";
    }
}
