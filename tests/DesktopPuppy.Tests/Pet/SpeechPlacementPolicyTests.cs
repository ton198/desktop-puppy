using DesktopPuppy.Native;
using DesktopPuppy.Pet.Placement;
using Xunit;

namespace DesktopPuppy.Tests.Pet;

public sealed class SpeechPlacementPolicyTests
{
    [Theory]
    [InlineData(400, 400, 340, 344)]
    [InlineData(400, 10, 340, 116)]
    [InlineData(0, 400, 0, 344)]
    [InlineData(910, 400, 790, 344)]
    public void Speech_prefers_above_flips_below_at_top_and_stays_in_work_area(int x, int y, int left, int top)
    {
        var actual = SpeechPlacementPolicy.Place(new PhysicalRect(x, y, x + 90, y + 100),
            210, 50, new PhysicalRect(0, 0, 1000, 800), 6);
        Assert.Equal(new PhysicalRect(left, top, left + 210, top + 50), actual);
    }

    [Fact]
    public void Speech_clamps_on_a_display_with_negative_coordinates()
    {
        var actual = SpeechPlacementPolicy.Place(new PhysicalRect(-90, 20, 0, 145),
            210, 60, new PhysicalRect(-1920, -200, 0, 880), 6);
        Assert.Equal(new PhysicalRect(-210, -46, 0, 14), actual);
    }
}
