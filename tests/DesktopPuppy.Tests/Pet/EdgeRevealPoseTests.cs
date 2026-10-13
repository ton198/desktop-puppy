using DesktopPuppy.Pet.Docking;
using Xunit;
namespace DesktopPuppy.Tests.Pet;
public sealed class EdgeRevealPoseTests
{
    [Theory]
    [InlineData(DockSide.Left)]
    [InlineData(DockSide.Right)]
    public void Reveal_visits_intermediate_poses_and_converges_to_idle(DockSide side)
    {
        var frames = Enumerable.Range(0, 101).Select(i => EdgeRevealPose.Frame(side, i / 100d)).Distinct().ToArray();
        Assert.Equal("idle", frames[0]);
        Assert.Contains($"edge-reveal-1-{side.ToString().ToLowerInvariant()}", frames);
        Assert.True(frames.Length >= 6);
        Assert.Equal($"edge-transition-{side.ToString().ToLowerInvariant()}", frames[^1]);
        Assert.Equal(0, EdgeRevealPose.LayoutBlend(0));
        Assert.InRange(EdgeRevealPose.LayoutBlend(.4), .01, .99);
        Assert.Equal(1, EdgeRevealPose.LayoutBlend(1));
    }
    [Fact]
    public void Reveal_finishes_after_settling_and_reversal_preserves_pose()
    {
        var c = new EdgeDockController();
        c.Dock(DockSide.Left, TimeSpan.Zero);
        c.Update(TimeSpan.FromMilliseconds(250), false, false);
        c.Update(TimeSpan.FromMilliseconds(300), true, false);
        c.Update(TimeSpan.FromMilliseconds(550), true, false);
        Assert.Equal(DockState.Expanding, c.State);
        Assert.InRange(c.Progress, .001, .5);
        var pose = EdgeRevealPose.Frame(c.Side, c.Progress);
        c.Update(TimeSpan.FromMilliseconds(550), false, false);
        Assert.Equal(pose, EdgeRevealPose.Frame(c.Side, c.Progress));
        c.Update(TimeSpan.FromMilliseconds(550), true, false);
        c.Update(TimeSpan.FromMilliseconds(900), true, false);
        Assert.Equal(DockState.Expanded, c.State);
        Assert.Equal("idle", EdgeRevealPose.Frame(c.Side, c.Progress));
    }
}
