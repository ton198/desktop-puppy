using DesktopPuppy.Native;

namespace DesktopPuppy.Pet.Docking;

/// <summary>Preserves the grabbed point in logical pixels while crossing physical DPI boundaries.</summary>
public readonly record struct EdgeDragAnchor(double GrabXDip, double GrabYDip)
{
    public static EdgeDragAnchor FromPose(int cursorX, int cursorY, PhysicalRect window, double scale,
        double poseLeft, double poseTop, double poseSize, double normalLeft, double normalTop, double normalSize)
    {
        if (scale <= 0 || poseSize <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        var x = (cursorX - window.Left) / scale;
        var y = (cursorY - window.Top) / scale;
        return new(normalLeft + (x - poseLeft) / poseSize * normalSize,
            normalTop + (y - poseTop) / poseSize * normalSize);
    }
    public static EdgeDragAnchor FromPress(int cursorX, int cursorY, PhysicalRect window, double scale)
        => new((cursorX - window.Left) / scale, (cursorY - window.Top) / scale);

    public PhysicalRect At(int cursorX, int cursorY, double scale, int currentWidth, int currentHeight)
    {
        var left = cursorX - (int)Math.Round(GrabXDip * scale);
        var top = cursorY - (int)Math.Round(GrabYDip * scale);
        return new PhysicalRect(left, top, left + currentWidth, top + currentHeight);
    }
}
