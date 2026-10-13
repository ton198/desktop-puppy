using DesktopPuppy.Pet.Docking;
using Xunit;
namespace DesktopPuppy.Tests.Pet;
public sealed class PetFacingControllerTests
{
    [Fact]
    public void Crossing_midpoint_turns_and_center_jitter_does_not_reverse()
    {
        var c = new PetFacingController();
        c.Update(200, 500, 16, TimeSpan.Zero);
        Assert.True(c.FacingRight);
        Assert.False(c.IsTurning);
        c.Update(510, 500, 16, TimeSpan.FromMilliseconds(10));
        Assert.False(c.IsTurning);
        c.Update(530, 500, 16, TimeSpan.FromMilliseconds(20));
        Assert.True(c.IsTurning);
        Assert.True(c.FacingRight);
        c.Update(501, 500, 16, TimeSpan.FromMilliseconds(140));
        Assert.True(c.IsTurning);
        c.Update(501, 500, 16, TimeSpan.FromMilliseconds(260));
        Assert.False(c.FacingRight);
        Assert.False(c.IsTurning);
        c.Update(490, 500, 16, TimeSpan.FromMilliseconds(300));
        Assert.False(c.IsTurning);
    }
    [Fact]
    public void Reversing_a_turn_continues_from_current_orientation()
    {
        var c = new PetFacingController();
        c.Update(200, 500, 16, TimeSpan.Zero);
        c.Update(530, 500, 16, TimeSpan.FromMilliseconds(10));
        c.Update(530, 500, 16, TimeSpan.FromMilliseconds(110));
        var orientation = c.Orientation;
        c.Update(470, 500, 16, TimeSpan.FromMilliseconds(110));
        Assert.Equal(orientation, c.Orientation);
        c.Update(470, 500, 16, TimeSpan.FromMilliseconds(350));
        Assert.True(c.FacingRight);
        Assert.False(c.IsTurning);
    }
}
