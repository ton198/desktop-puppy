using DesktopPuppy.Pet.Placement;
using Xunit;

namespace DesktopPuppy.Tests;

public sealed class PetPlacementPolicyTests
{
    private static readonly MonitorWorkArea Primary = new("primary", 0, 0, 1920, 1080, true);

    [Fact]
    public void Clamp_keeps_at_least_twenty_pixels_visible_on_each_edge()
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
