using DesktopPuppy.Pet.Docking;
using DesktopPuppy.Native;
using Xunit;

namespace DesktopPuppy.Tests.Pet;

public sealed class EdgeDockControllerTests
{
    [Theory]
    [InlineData(23, 120, 0, 1000, 1, DockSide.Left)]
    [InlineData(25, 120, 0, 1000, 1, DockSide.None)]
    [InlineData(880, 977, 0, 1000, 1, DockSide.Right)]
    [InlineData(-1965, -1850, -2000, 0, 1.5, DockSide.Left)]
    public void Detection_uses_visible_bounds_and_dpi(double left, double right, double workLeft, double workRight, double scale, DockSide expected)
        => Assert.Equal(expected, EdgeDockController.Detect(left, right, workLeft, workRight, scale));

    [Fact]
    public void Docking_and_hover_reveal_take_250ms()
    {
        var c = new EdgeDockController();
        c.Dock(DockSide.Left, TimeSpan.Zero);
        c.Update(TimeSpan.FromMilliseconds(250), false, false);
        Assert.Equal(DockState.Collapsed, c.State);
        Assert.Equal(1, c.Progress);
        c.Update(TimeSpan.FromMilliseconds(300), true, false);
        c.Update(TimeSpan.FromMilliseconds(550), true, false);
        Assert.Equal(DockState.Expanded, c.State);
        Assert.Equal(0, c.Progress);
    }

    [Fact]
    public void Pointer_leave_waits_800ms_and_bubble_keeps_pet_expanded()
    {
        var c = Expanded();
        c.Update(TimeSpan.FromMilliseconds(300), false, false);
        c.Update(TimeSpan.FromMilliseconds(1099), false, false);
        Assert.Equal(DockState.Expanded, c.State);
        c.Update(TimeSpan.FromMilliseconds(1100), false, false);
        Assert.Equal(DockState.Collapsing, c.State);
        c.Update(TimeSpan.FromMilliseconds(1200), false, true);
        c.Update(TimeSpan.FromMilliseconds(1450), false, true);
        Assert.Equal(DockState.Expanded, c.State);
        c.Update(TimeSpan.FromMilliseconds(1600), false, false);
        c.Update(TimeSpan.FromMilliseconds(2399), false, false);
        Assert.Equal(0, c.Progress);
    }

    [Fact]
    public void Reversal_keeps_position_continuous_and_reset_cancels_docking()
    {
        var c = new EdgeDockController();
        c.Dock(DockSide.Right, TimeSpan.Zero);
        c.Update(TimeSpan.FromMilliseconds(100), false, false);
        var progress = c.Progress;
        c.Update(TimeSpan.FromMilliseconds(100), true, false);
        Assert.Equal(progress, c.Progress);
        c.Reset();
        c.Update(TimeSpan.FromSeconds(10), false, false);
        Assert.Equal(DockSide.None, c.Side);
        Assert.Equal(DockState.Free, c.State);
        Assert.Equal(0, c.Progress);
    }

    [Fact]
    public void Keep_expanded_cancels_motion_immediately_for_pointer_press()
    {
        var c = new EdgeDockController();
        c.Dock(DockSide.Left, TimeSpan.Zero);
        c.Update(TimeSpan.FromMilliseconds(100), false, false);
        c.KeepExpanded(TimeSpan.FromMilliseconds(100));
        Assert.Equal(0, c.Progress);
        Assert.Equal(DockState.Expanded, c.State);
    }

    private static EdgeDockController Expanded()
    {
        var c = new EdgeDockController();
        c.Dock(DockSide.Left, TimeSpan.Zero);
        c.KeepExpanded(TimeSpan.Zero);
        return c;
    }

    [Fact]
    public void Drag_across_dpi_keeps_grab_in_dips_and_uses_current_window_size()
    {
        var anchor = EdgeDragAnchor.FromPress(18, 279, new PhysicalRect(-153, 150, 107, 360), 1);
        var atOriginalDpi = anchor.At(318, 339, 1, 260, 210);
        Assert.Equal(new PhysicalRect(147, 210, 407, 420), atOriginalDpi);
        var atDoubleDpi = anchor.At(318, 339, 2, 520, 420);
        Assert.Equal(new PhysicalRect(-24, 81, 496, 501), atDoubleDpi);
        Assert.Equal(520, atDoubleDpi.Width);
    }
}
