using DesktopPuppy.Pet.Placement;
using Xunit;

namespace DesktopPuppy.Tests.Pet;

public sealed class DpiGeometryPolicyTests
{
    [Fact]
    public void One_hundred_percent_scale_is_identity()
    {
        Assert.Equal(123.5, DpiGeometryPolicy.ToDevicePixels(123.5, 1.0));
        Assert.Equal(-40.25, DpiGeometryPolicy.ToDips(-40.25, 1.0));
    }

    [Fact]
    public void Dip_to_device_conversion_applies_explicit_non_integer_scale()
    {
        Assert.Equal(100, DpiGeometryPolicy.ToDevicePixels(80, 1.25));
    }

    [Fact]
    public void Device_to_dip_conversion_applies_explicit_scale()
    {
        Assert.Equal(200, DpiGeometryPolicy.ToDips(300, 1.5));
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Coordinate_round_trip_preserves_fractional_geometry(double scale)
    {
        const double dips = 123.456;

        var recovered = DpiGeometryPolicy.ToDips(DpiGeometryPolicy.ToDevicePixels(dips, scale), scale);

        Assert.InRange(Math.Abs(recovered - dips), 0, 1e-9);
    }

    [Fact]
    public void Visible_edge_margin_is_twenty_physical_pixels_at_one_hundred_fifty_percent_scale()
    {
        const double scale = 1.5;
        var visibleMarginDips = DpiGeometryPolicy.ToDips(20, scale);
        var workArea = new MonitorWorkArea("scaled", 0, 0, 1280, 720, true);
        const double petWidth = 260;
        const double petHeight = 210;
        var clamped = new[]
        {
            PetPlacementPolicy.Clamp(new PetPosition(-1000, 100), petWidth, petHeight, workArea, visibleMarginDips),
            PetPlacementPolicy.Clamp(new PetPosition(2000, 100), petWidth, petHeight, workArea, visibleMarginDips),
            PetPlacementPolicy.Clamp(new PetPosition(100, -1000), petWidth, petHeight, workArea, visibleMarginDips),
            PetPlacementPolicy.Clamp(new PetPosition(100, 2000), petWidth, petHeight, workArea, visibleMarginDips)
        };

        Assert.InRange((clamped[0].Left + petWidth - workArea.Left) * scale, 20 - 1e-9, 20 + 1e-9);
        Assert.InRange((workArea.Left + workArea.Width - clamped[1].Left) * scale, 20 - 1e-9, 20 + 1e-9);
        Assert.InRange((clamped[2].Top + petHeight - workArea.Top) * scale, 20 - 1e-9, 20 + 1e-9);
        Assert.InRange((workArea.Top + workArea.Height - clamped[3].Top) * scale, 20 - 1e-9, 20 + 1e-9);
    }

    [Fact]
    public void Same_physical_visible_margin_is_independent_of_monitor_scale()
    {
        var visibleAtOneX = ClampLeftEdgeAndMeasurePhysicalVisibility(1.0);
        var visibleAtOneAndHalfX = ClampLeftEdgeAndMeasurePhysicalVisibility(1.5);

        Assert.InRange(visibleAtOneX, 20 - 1e-9, 20 + 1e-9);
        Assert.InRange(visibleAtOneAndHalfX, 20 - 1e-9, 20 + 1e-9);
    }

    private static double ClampLeftEdgeAndMeasurePhysicalVisibility(double scale)
    {
        var area = new MonitorWorkArea(
            "monitor", 0, 0,
            DpiGeometryPolicy.ToDips(1920, scale),
            DpiGeometryPolicy.ToDips(1080, scale),
            true);
        var petWidth = DpiGeometryPolicy.ToDips(260, scale);
        var petHeight = DpiGeometryPolicy.ToDips(210, scale);
        var minimumVisible = DpiGeometryPolicy.ToDips(20, scale);
        var position = PetPlacementPolicy.Clamp(
            new PetPosition(-10000, 100), petWidth, petHeight, area, minimumVisible);

        return (position.Left + petWidth - area.Left) * scale;
    }
}
