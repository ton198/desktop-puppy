namespace DesktopPuppy.Pet.Placement;

public static class DpiGeometryPolicy
{
    public static double ToDevicePixels(double deviceIndependentPixels, double dpiScale)
    {
        ValidateScale(dpiScale);
        return deviceIndependentPixels * dpiScale;
    }

    public static double ToDips(double devicePixels, double dpiScale)
    {
        ValidateScale(dpiScale);
        return devicePixels / dpiScale;
    }

    private static void ValidateScale(double dpiScale)
    {
        if (!double.IsFinite(dpiScale) || dpiScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpiScale), "DPI scale must be a finite positive value.");
        }
    }
}
