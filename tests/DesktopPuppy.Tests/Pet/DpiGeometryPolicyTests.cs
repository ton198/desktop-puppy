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
    [InlineData(100.0, 96, 100)]
    [InlineData(100.01, 96, 101)]
    [InlineData(100.0, 144, 150)]
    [InlineData(100.01, 144, 151)]
    public void Dip_extents_convert_to_ceiling_physical_pixels(double dips, uint dpi, int expected)
    {
        Assert.Equal(expected, DpiGeometryPolicy.ToPhysicalExtent(dips, dpi));
    }

    [Theory]
    [InlineData(10.5, 11)]
    [InlineData(-10.5, -11)]
    [InlineData(10.49, 10)]
    [InlineData(-10.49, -10)]
    public void Physical_coordinates_round_away_from_zero(double coordinate, int expected)
    {
        Assert.Equal(expected, DpiGeometryPolicy.RoundPhysicalCoordinate(coordinate));
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
        const uint dpi = 144;
        const double scale = dpi / 96.0;
        var workArea = new MonitorWorkArea("scaled", 0, 0, 1920, 1080, true);
        var petWidth = DpiGeometryPolicy.ToPhysicalExtent(260, dpi);
        var petHeight = DpiGeometryPolicy.ToPhysicalExtent(210, dpi);
        const double minimumVisiblePixels = 20;
        var clamped = new[]
        {
            PetPlacementPolicy.Clamp(new PetPosition(-1000, 100), petWidth, petHeight, workArea, minimumVisiblePixels),
            PetPlacementPolicy.Clamp(new PetPosition(2000, 100), petWidth, petHeight, workArea, minimumVisiblePixels),
            PetPlacementPolicy.Clamp(new PetPosition(100, -1000), petWidth, petHeight, workArea, minimumVisiblePixels),
            PetPlacementPolicy.Clamp(new PetPosition(100, 2000), petWidth, petHeight, workArea, minimumVisiblePixels)
        };

        Assert.Equal(1.5, scale);
        Assert.Equal(20, clamped[0].Left + petWidth - workArea.Left);
        Assert.Equal(20, workArea.Left + workArea.Width - clamped[1].Left);
        Assert.Equal(20, clamped[2].Top + petHeight - workArea.Top);
        Assert.Equal(20, workArea.Top + workArea.Height - clamped[3].Top);
    }

    [Fact]
    public void Same_physical_visible_margin_is_independent_of_monitor_scale()
    {
        var visibleAtOneX = ClampLeftEdgeAndMeasurePhysicalVisibility(96);
        var visibleAtOneAndHalfX = ClampLeftEdgeAndMeasurePhysicalVisibility(144);

        Assert.Equal(20, visibleAtOneX);
        Assert.Equal(20, visibleAtOneAndHalfX);
    }

    private static double ClampLeftEdgeAndMeasurePhysicalVisibility(uint dpi)
    {
        var workArea = new MonitorWorkArea("monitor", 0, 0, 1920, 1080, true);
        var petWidth = DpiGeometryPolicy.ToPhysicalExtent(260, dpi);
        var petHeight = DpiGeometryPolicy.ToPhysicalExtent(210, dpi);
        var position = PetPlacementPolicy.Clamp(
            new PetPosition(-10000, 100), petWidth, petHeight, workArea, minimumVisiblePixels: 20);

        return position.Left + petWidth - workArea.Left;
    }
}
