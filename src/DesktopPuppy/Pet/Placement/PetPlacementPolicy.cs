using DesktopPuppy.Native;

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
        if (!double.IsFinite(petWidth) || petWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(petWidth));
        }

        if (!double.IsFinite(petHeight) || petHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(petHeight));
        }

        if (!double.IsFinite(requested.Left) || !double.IsFinite(requested.Top))
        {
            throw new ArgumentOutOfRangeException(nameof(requested), "Requested physical coordinates must be finite.");
        }

        if (!double.IsFinite(workArea.Left) || !double.IsFinite(workArea.Top) ||
            !double.IsFinite(workArea.Width) || workArea.Width <= 0 ||
            !double.IsFinite(workArea.Height) || workArea.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workArea), "Work-area coordinates and extents must be finite, with positive dimensions.");
        }

        var workAreaRight = workArea.Left + workArea.Width;
        var workAreaBottom = workArea.Top + workArea.Height;
        if (!double.IsFinite(workAreaRight) || workAreaRight <= workArea.Left ||
            !double.IsFinite(workAreaBottom) || workAreaBottom <= workArea.Top)
        {
            throw new ArgumentOutOfRangeException(nameof(workArea), "Work-area edges must form finite positive extents.");
        }

        if (!double.IsFinite(minimumVisiblePixels) || minimumVisiblePixels < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumVisiblePixels));
        }

        var minLeft = workArea.Left - petWidth + minimumVisiblePixels;
        var maxLeft = workAreaRight - minimumVisiblePixels;
        var minTop = workArea.Top - petHeight + minimumVisiblePixels;
        var maxTop = workAreaBottom - minimumVisiblePixels;

        if (!double.IsFinite(minLeft) || !double.IsFinite(maxLeft) ||
            !double.IsFinite(minTop) || !double.IsFinite(maxTop))
        {
            throw new ArgumentOutOfRangeException(nameof(workArea), "Work-area and window geometry must produce finite placement limits.");
        }

        return new PetPosition(
            Math.Clamp(requested.Left, minLeft, maxLeft),
            Math.Clamp(requested.Top, minTop, maxTop));
    }
}
