namespace DesktopPuppy.Pet.Placement;

/// <summary>Describes a monitor work area in physical pixels.</summary>
public readonly record struct MonitorWorkArea(
    string Id,
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsPrimary);

/// <summary>Describes a window position in physical pixels.</summary>
public readonly record struct PetPosition(double Left, double Top);

public static class PetPlacementPolicy
{
    public const double DefaultMinimumVisiblePixels = 20;

    public static MonitorWorkArea ResolveMonitor(
        string? savedMonitorId,
        IReadOnlyList<MonitorWorkArea> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        }

        if (!string.IsNullOrWhiteSpace(savedMonitorId))
        {
            var savedMonitor = monitors.FirstOrDefault(monitor => monitor.Id == savedMonitorId);
            if (!string.IsNullOrWhiteSpace(savedMonitor.Id))
            {
                return savedMonitor;
            }
        }

        return monitors.FirstOrDefault(monitor => monitor.IsPrimary);
    }

    /// <summary>Clamps physical-pixel window geometry while keeping the requested physical-pixel edge visible.</summary>
    public static PetPosition Clamp(
        PetPosition requested,
        double petWidth,
        double petHeight,
        MonitorWorkArea workArea,
        double minimumVisiblePixels = DefaultMinimumVisiblePixels)
    {
        if (petWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(petWidth));
        }

        if (petHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(petHeight));
        }

        if (minimumVisiblePixels < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumVisiblePixels));
        }

        var minLeft = workArea.Left - petWidth + minimumVisiblePixels;
        var maxLeft = workArea.Left + workArea.Width - minimumVisiblePixels;
        var minTop = workArea.Top - petHeight + minimumVisiblePixels;
        var maxTop = workArea.Top + workArea.Height - minimumVisiblePixels;

        return new PetPosition(
            Math.Clamp(requested.Left, minLeft, maxLeft),
            Math.Clamp(requested.Top, minTop, maxTop));
    }
}
