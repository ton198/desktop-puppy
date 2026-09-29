using DesktopPuppy.Pet.Placement;
using Xunit;

namespace DesktopPuppy.Tests;

public sealed class PetPlacementPolicyTests
{
    private static readonly MonitorWorkArea Primary = new("primary", 0, 0, 1920, 1080, true);

    // Purpose: Keep left/above displays selectable and clampable.
    // Coverage: Negative physical monitor origin and an off-screen request.
    // Target effect: The selected secondary monitor retains its negative virtual-screen coordinates.
    [Fact]
    public void Negative_origin_secondary_monitor_remains_selectable_and_clampable()
    {
        var secondary = new MonitorWorkArea("left", -1920, -200, 1920, 1080, false);
        var selected = PetPlacementPolicy.ResolveMonitor("left", new[] { Primary, secondary });

        var position = PetPlacementPolicy.Clamp(new PetPosition(-5000, -5000), 260, 210, selected);

        Assert.Equal(secondary, selected);
        Assert.Equal(-2160, position.Left);
        Assert.Equal(-390, position.Top);
    }

    // Purpose: Keep a partially off-screen window accessible at each work-area edge.
    // Coverage: Extreme requested left and bottom positions.
    // Target effect: At least 20 physical pixels remain visible.
    [Fact]
    public void Clamp_keeps_at_least_twenty_physical_pixels_visible_on_each_edge()
    {
        var position = PetPlacementPolicy.Clamp(
            new PetPosition(-500, 1200),
            petWidth: 260,
            petHeight: 210,
            Primary);

        Assert.Equal(-240, position.Left);
        Assert.Equal(1060, position.Top);
    }

    // Purpose: Make the pure placement policy's physical-pixel unit contract explicit.
    // Coverage: Negative-origin work area, window extents, requested position, and physical margin.
    // Target effect: Clamp results use the supplied physical values directly.
    [Fact]
    public void Clamp_interprets_window_and_work_area_geometry_as_physical_pixels()
    {
        var workArea = new MonitorWorkArea("left", -1920, -200, 1920, 1080, false);

        var position = PetPlacementPolicy.Clamp(
            new PetPosition(-5000, -5000),
            petWidth: 300,
            petHeight: 200,
            workArea,
            minimumVisiblePixels: 20);

        Assert.Equal(-2200, position.Left);
        Assert.Equal(-380, position.Top);
    }

    // Purpose: Prevent NaN and infinite extents from passing numeric positivity checks.
    // Coverage: Non-finite pet width and height values.
    // Target effect: Invalid physical dimensions throw ArgumentOutOfRangeException.
    [Fact]
    public void Clamp_rejects_non_finite_pet_dimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PetPlacementPolicy.Clamp(new PetPosition(0, 0), double.NaN, 100, Primary));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PetPlacementPolicy.Clamp(new PetPosition(0, 0), double.PositiveInfinity, 100, Primary));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PetPlacementPolicy.Clamp(new PetPosition(0, 0), 100, double.NaN, Primary));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PetPlacementPolicy.Clamp(new PetPosition(0, 0), 100, double.PositiveInfinity, Primary));
    }

    // Purpose: Prevent non-finite requested coordinates from reaching clamp arithmetic.
    // Coverage: NaN and infinite physical left/top requests.
    // Target effect: Invalid requested positions throw ArgumentOutOfRangeException.
    [Fact]
    public void Clamp_rejects_non_finite_requested_coordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PetPlacementPolicy.Clamp(new PetPosition(double.NaN, 0), 100, 100, Primary));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PetPlacementPolicy.Clamp(new PetPosition(0, double.PositiveInfinity), 100, 100, Primary));
    }

    // Purpose: Keep invalid monitor work-area geometry out of placement math.
    // Coverage: Non-finite origins/extents and non-positive work-area dimensions.
    // Target effect: Invalid physical work areas throw ArgumentOutOfRangeException.
    [Fact]
    public void Clamp_rejects_invalid_work_area_geometry()
    {
        AssertInvalidWorkArea(new MonitorWorkArea("nan-left", double.NaN, 0, 100, 100, true));
        AssertInvalidWorkArea(new MonitorWorkArea("infinite-top", 0, double.PositiveInfinity, 100, 100, true));
        AssertInvalidWorkArea(new MonitorWorkArea("nan-width", 0, 0, double.NaN, 100, true));
        AssertInvalidWorkArea(new MonitorWorkArea("infinite-height", 0, 0, 100, double.PositiveInfinity, true));
        AssertInvalidWorkArea(new MonitorWorkArea("zero-width", 0, 0, 0, 100, true));
        AssertInvalidWorkArea(new MonitorWorkArea("negative-height", 0, 0, 100, -1, true));
    }

    // Purpose: Prevent a non-finite visible-edge margin from making every position appear valid.
    // Coverage: NaN and infinite physical margins.
    // Target effect: Invalid margins throw ArgumentOutOfRangeException.
    [Fact]
    public void Clamp_rejects_non_finite_visible_margin()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PetPlacementPolicy.Clamp(new PetPosition(0, 0), 100, 100, Primary, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PetPlacementPolicy.Clamp(new PetPosition(0, 0), 100, 100, Primary, double.PositiveInfinity));
    }

    // Purpose: Preserve a deterministic recovery monitor after a saved display disappears.
    // Coverage: Unknown prior ID plus one current primary display.
    // Target effect: Monitor resolution selects the current primary monitor.
    [Fact]
    public void Missing_saved_monitor_falls_back_to_primary_monitor()
    {
        var monitors = new[]
        {
            new MonitorWorkArea("secondary", 1920, 0, 1920, 1080, false),
            Primary
        };

        var result = PetPlacementPolicy.ResolveMonitor("removed-monitor", monitors);

        Assert.Equal(Primary, result);
    }

    private static void AssertInvalidWorkArea(MonitorWorkArea workArea)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PetPlacementPolicy.Clamp(new PetPosition(0, 0), 100, 100, workArea));
    }
}
