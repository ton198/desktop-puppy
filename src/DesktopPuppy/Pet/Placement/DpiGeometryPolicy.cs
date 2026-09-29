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

    public static int ToPhysicalExtent(double deviceIndependentPixels, uint dpi)
    {
        if (!double.IsFinite(deviceIndependentPixels) || deviceIndependentPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceIndependentPixels));
        }

        var scale = dpi / 96.0;
        ValidateScale(scale);
        return checked((int)Math.Ceiling(ToDevicePixels(deviceIndependentPixels, scale)));
    }

    public static int RoundPhysicalCoordinate(double physicalCoordinate)
    {
        if (!double.IsFinite(physicalCoordinate))
        {
            throw new ArgumentOutOfRangeException(nameof(physicalCoordinate));
        }

        return checked((int)Math.Round(physicalCoordinate, MidpointRounding.AwayFromZero));
    }

    private static void ValidateScale(double dpiScale)
    {
        if (!double.IsFinite(dpiScale) || dpiScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpiScale), "DPI scale must be a finite positive value.");
        }
    }
}
