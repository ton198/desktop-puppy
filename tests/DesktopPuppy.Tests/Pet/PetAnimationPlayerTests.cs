using DesktopPuppy.Pet.Animation;
using Xunit;
namespace DesktopPuppy.Tests.Pet;
public class PetAnimationPlayerTests
{
    [Fact]
    public void Completed_animation_returns_to_idle_and_new_action_interrupts_current_action()
    {
        var player = new PetAnimationPlayer();
        player.Play("hit", TimeSpan.Zero);
        Assert.Equal("hit-startle", player.FrameAt(TimeSpan.Zero));
        player.Play("click", TimeSpan.FromMilliseconds(50));
        Assert.Equal("click-smug", player.FrameAt(TimeSpan.FromMilliseconds(50)));
        Assert.Equal("idle", player.FrameAt(TimeSpan.FromSeconds(10)));
    }
    [Fact]
    public void Drag_pose_is_held_until_landing_replaces_it()
    {
        var player = new PetAnimationPlayer();
        player.Play("drag", TimeSpan.Zero);
        Assert.Equal("drag-lift", player.FrameAt(TimeSpan.FromHours(1)));
        player.Play("landing", TimeSpan.FromHours(1));
        Assert.Equal("landing", player.FrameAt(TimeSpan.FromHours(1)));
        Assert.Equal("idle", player.FrameAt(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(2)));
    }
}
