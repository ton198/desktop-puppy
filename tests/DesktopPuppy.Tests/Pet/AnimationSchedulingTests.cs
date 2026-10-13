using DesktopPuppy.Pet.Animation;
using DesktopPuppy.Pet.Docking;
using DesktopPuppy.Native;
using Xunit;

namespace DesktopPuppy.Tests.Pet;

public sealed class AnimationSchedulingTests
{
    [Fact]
    public void Held_drag_and_finished_actions_do_not_need_frame_timer()
    {
        var player = new PetAnimationPlayer();
        player.Play("hit", TimeSpan.Zero);
        Assert.True(player.NeedsFrames(TimeSpan.Zero));
        Assert.False(player.NeedsFrames(TimeSpan.FromMinutes(1)));
        player.Play("drag", TimeSpan.Zero);
        Assert.False(player.NeedsFrames(TimeSpan.Zero));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void Undocking_maps_grabbed_pose_point_into_expanded_image(double dpi)
    {
        var window = new PhysicalRect(0, 0, 200, 200);
        var anchor = EdgeDragAnchor.FromPose((int)(30 * dpi), (int)(40 * dpi), window, dpi,
            -20, 0, 100, -80, -100, 300);
        // Pose fractions .5/.4 become normal-layout coordinates 70/20 DIP.
        Assert.Equal(70, anchor.GrabXDip, 6);
        Assert.Equal(20, anchor.GrabYDip, 6);
        var moved = anchor.At((int)(100 * dpi), (int)(100 * dpi), dpi, 200, 200);
        Assert.Equal((int)(30 * dpi), moved.Left);
        Assert.Equal((int)(80 * dpi), moved.Top);
    }
}
