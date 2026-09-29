using DesktopPuppy.Pet.Placement;
using Xunit;

namespace DesktopPuppy.Tests;

public sealed class PetPlacementPolicyTests
{
    private static readonly MonitorWorkArea Primary = new("primary", 0, 0, 1920, 1080, true);

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
}
