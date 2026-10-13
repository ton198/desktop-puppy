using DesktopPuppy.Runtime;
using Xunit;
namespace DesktopPuppy.Tests.Runtime;
public sealed class RuntimeTests
{
    [Fact] public void Fullscreen_blocks_show_and_requests_without_losing_user_visibility()
    {
        var state=new RuntimeState(); Assert.True(state.ShouldShow); state.Fullscreen=true; Assert.False(state.ShouldShow); Assert.False(state.CanRequest); state.Fullscreen=false; Assert.True(state.ShouldShow);
        state.Paused=true; Assert.True(state.ShouldShow); Assert.False(state.CanProactive); state.UserHidden=true; Assert.False(state.ShouldShow);
    }
    [Fact] public void Locked_and_suspended_sessions_cannot_consume_proactive_triggers()
    {
        var state=new RuntimeState();state.Locked=true;Assert.False(state.CanProactive);
        state.Suspended=true;state.Suspended=false;Assert.False(state.CanRequest);
        state.Locked=false;Assert.True(state.CanProactive);
    }
    [Fact] public async Task Explicit_show_cancels_startup_wait_and_shows_once()
    {
        var state=new RuntimeState(); int shown=0; using var delay=new StartupDelayCoordinator(state,()=>shown++,TimeSpan.FromHours(1));
        var pending=delay.StartAsync(); Assert.False(state.ShouldShow); delay.ShowNow(); await pending; Assert.True(state.ShouldShow); Assert.Equal(1,shown);
    }
    [Fact] public void Startup_command_quotes_executable_with_spaces()
    {
        Assert.Equal("\"C:\\Program Files\\Desktop Puppy\\Puppy.exe\" --startup",StartupRegistrationService.BuildCommand("C:\\Program Files\\Desktop Puppy\\Puppy.exe"));
    }
}
