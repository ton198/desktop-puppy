using DesktopPuppy.Native;
using Xunit;

namespace DesktopPuppy.Tests.Native;

public sealed class MonitorDisplayContractTests
{
    // Purpose: Prevent monitors left/above the primary display from being treated as invalid.
    // Coverage: Negative left/top with right/bottom beyond origin.
    // Target effect: Width and height are exact positive physical-pixel extents.
    [Fact]
    public void PhysicalRect_reports_extents_for_negative_virtual_coordinates()
    {
        var rectangle = new PhysicalRect(-1920, -200, 0, 880);

        Assert.True(rectangle.IsValid);
        Assert.Equal(1920, rectangle.Width);
        Assert.Equal(1080, rectangle.Height);
    }

    // Purpose: Keep invalid native geometry out of placement math.
    // Coverage: Equal and reversed rectangle edges.
    // Target effect: IsValid is false and MonitorDisplayInfo rejects invalid work areas.
    [Fact]
    public void PhysicalRect_marks_empty_or_inverted_bounds_invalid()
    {
        Assert.False(new PhysicalRect(10, 20, 10, 30).IsValid);
        Assert.False(new PhysicalRect(10, 20, 30, 20).IsValid);
        Assert.False(new PhysicalRect(10, 20, 9, 30).IsValid);
        Assert.False(new PhysicalRect(10, 20, 30, 19).IsValid);

        var bounds = new PhysicalRect(0, 0, 100, 100);
        var invalidWorkArea = new PhysicalRect(20, 20, 20, 80);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MonitorDisplayInfo("DISPLAY1", bounds, invalidWorkArea, isPrimary: true));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MonitorDisplayInfo("DISPLAY1", invalidWorkArea, bounds, isPrimary: true));
        Assert.Throws<ArgumentException>(() =>
            new MonitorDisplayInfo("  ", bounds, bounds, isPrimary: true));
    }

    // Purpose: Preserve native monitor geometry without losing which display is primary.
    // Coverage: Valid bounds/work area and primary flag.
    // Target effect: Immutable record preserves each value.
    [Fact]
    public void MonitorDisplayInfo_keeps_physical_bounds_and_primary_flag()
    {
        var bounds = new PhysicalRect(-1920, -100, 0, 980);
        var workArea = new PhysicalRect(-1920, -60, 0, 980);

        var monitor = new MonitorDisplayInfo("\\\\.\\DISPLAY2", bounds, workArea, isPrimary: false);

        Assert.Equal("\\\\.\\DISPLAY2", monitor.DeviceId);
        Assert.Equal(bounds, monitor.Bounds);
        Assert.Equal(workArea, monitor.WorkArea);
        Assert.False(monitor.IsPrimary);
    }
}
