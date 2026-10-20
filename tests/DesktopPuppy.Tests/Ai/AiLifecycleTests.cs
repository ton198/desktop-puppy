using DesktopPuppy.Ai;
using DesktopPuppy.Native;
using Xunit;

namespace DesktopPuppy.Tests.Ai;

public sealed class AiLifecycleTests
{
    [Fact]
    public void One_request_remains_in_flight_after_placeholder_expires()
    {
        var gate = new SingleBubbleRequestGate(); var id = gate.TryBegin(TimeSpan.Zero)!.Value;
        Assert.Null(gate.TryBegin(TimeSpan.FromSeconds(1)));
        Assert.True(gate.Expire(TimeSpan.FromSeconds(15)));
        Assert.Null(gate.TryBegin(TimeSpan.FromSeconds(16)));
        Assert.False(gate.CanDisplay(id, TimeSpan.FromSeconds(16), true));
        gate.Complete(id); Assert.NotNull(gate.TryBegin(TimeSpan.FromSeconds(17)));
    }

    [Fact]
    public void Dismiss_replacement_or_hide_prevents_late_result()
    {
        var gate = new SingleBubbleRequestGate(); var id = gate.TryBegin(TimeSpan.Zero)!.Value;
        Assert.True(gate.CanDisplay(id, TimeSpan.FromSeconds(14), true));
        Assert.False(gate.CanDisplay(id, TimeSpan.FromSeconds(14), false));
        gate.Invalidate(); Assert.False(gate.CanDisplay(id, TimeSpan.FromSeconds(14), true));
        gate.Complete(id); var next = gate.TryBegin(TimeSpan.FromSeconds(14))!.Value;
        Assert.False(gate.CanDisplay(id, TimeSpan.FromSeconds(14), true)); Assert.True(gate.CanDisplay(next, TimeSpan.FromSeconds(14), true));
    }

    [Fact]
    public void Fullscreen_restore_respects_user_hidden_state()
    {
        var visibility = new PetVisibilityState(); Assert.True(visibility.ShouldShow);
        visibility.SetFullscreen(true); Assert.False(visibility.ShouldShow);
        visibility.SetUserHidden(true); visibility.SetFullscreen(false); Assert.False(visibility.ShouldShow);
        visibility.SetUserHidden(false); Assert.True(visibility.ShouldShow);
        visibility.SetFullscreen(true); visibility.SetUserHidden(false); Assert.False(visibility.ShouldShow);
    }

    [Theory]
    [InlineData(true, true, false, false, false, false)]
    [InlineData(true, false, true, false, false, false)]
    [InlineData(true, false, false, true, false, false)]
    [InlineData(true, false, false, false, true, true)]
    [InlineData(false, false, false, false, true, false)]
    public void Borderless_policy_filters_ordinary_windows(bool visible, bool minimized, bool cloaked, bool framed, bool covers, bool expected)
    {
        var bounds = new PhysicalRect(1920, 0, 3840, 1080); var window = covers ? bounds : new PhysicalRect(1920, 0, 3840, 1040);
        Assert.Equal(expected, FullscreenPolicy.IsBorderlessFullscreen(new(window, visible, minimized, cloaked, framed, false), [bounds]));
    }

    [Fact]
    public void Borderless_on_second_monitor_or_exclusive_direct3d_suppresses()
    {
        var screens = new[] { new PhysicalRect(0, 0, 1920, 1080), new PhysicalRect(-1920, 0, 0, 1080) };
        Assert.True(FullscreenPolicy.IsBorderlessFullscreen(new(screens[1], true, false, false, false, false), screens));
        Assert.True(FullscreenPolicy.ShouldSuppress(3, [], screens));
        Assert.False(FullscreenPolicy.ShouldSuppress(2, [], screens)); // busy/presentation is not evidence of a fullscreen window
    }

    [Theory]
    [InlineData(0x00000020L)] // WS_EX_TRANSPARENT: click-through overlay
    [InlineData(0x00000080L)] // WS_EX_TOOLWINDOW: auxiliary tool, not a fullscreen app
    [InlineData(0x08000000L)] // WS_EX_NOACTIVATE: passive overlay
    [InlineData(0x080800A0L)] // transparent layered tool overlay used in the EXE reproduction
    public void Screen_covering_overlay_does_not_hide_pet(long extendedStyle)
    {
        var screen = new PhysicalRect(0, 0, 2560, 1600);
        var overlay = new FullscreenWindowInfo(screen, true, false, false, false, false, extendedStyle);
        Assert.False(FullscreenPolicy.IsBorderlessFullscreen(overlay, [screen]));
        Assert.False(FullscreenPolicy.ShouldSuppress(2, [overlay], [screen]));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(0x00000008L)] // WS_EX_TOPMOST alone is normal for fullscreen applications
    [InlineData(0x00080000L)] // WS_EX_LAYERED alone does not imply a passive overlay
    public void Ordinary_fullscreen_still_hides_pet_when_taskbar_is_auto_hidden(long extendedStyle)
    {
        var screen = new PhysicalRect(0, 0, 2560, 1600);
        var fullscreen = new FullscreenWindowInfo(screen, true, false, false, false, false, extendedStyle);
        Assert.True(FullscreenPolicy.ShouldSuppress(2, [fullscreen], [screen]));
    }
}
