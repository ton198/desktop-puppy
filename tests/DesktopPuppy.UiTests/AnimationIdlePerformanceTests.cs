using System.Diagnostics;
using System.Text.Json;
using DesktopPuppy.Core.Settings;
using DesktopPuppy.Settings;
using Xunit;
namespace DesktopPuppy.UiTests;
public sealed class AnimationIdlePerformanceTests
{
    [Fact]
    [Trait("Category","Performance")]
    public async Task Fifteen_minutes_of_idle_actions_do_not_keep_frame_timer_running_while_stationary()
    {
        using var seed=new UsageStatisticsSeed();
        var settings=new SettingsService(seed.DatabasePath);
        await settings.SaveAsync(new AppSettings {Activity=new ActivitySettings {Enabled=false},Ai=new AiSettings {Enabled=false}});
        var executable=Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE");Assert.False(string.IsNullOrWhiteSpace(executable));
        using var harness=DesktopPuppyUiHarness.Start(executable!,seed.DatabasePath);
        var pet=harness.WaitForPetWindow(TimeSpan.FromSeconds(5));
        harness.WaitForAutomationName(pet,"PetAnimation",name=>name=="idle",TimeSpan.FromSeconds(10));
        var initial=Read(harness,pet);
        Assert.True(initial.TotalTicks>0,"Startup animation must produce measured frame ticks.");
        var elapsed=Stopwatch.StartNew();
        while(elapsed.Elapsed<TimeSpan.FromMinutes(15))
        {
            var remaining=TimeSpan.FromMinutes(15)-elapsed.Elapsed;
            Thread.Sleep(remaining<TimeSpan.FromSeconds(30)?remaining:TimeSpan.FromSeconds(30));
            var observed=Read(harness,pet);
            Assert.True(observed.MaxConsecutiveStationaryTicks<=2,$"Frame timer kept running without animation: {observed.MaxConsecutiveStationaryTicks} consecutive stationary ticks.");
        }
        var final=Read(harness,pet);
        Assert.True(final.TotalTicks>initial.TotalTicks,"Idle actions must still animate during the measurement.");
        Assert.True(final.MaxConsecutiveStationaryTicks<=2);
    }
    private static Counters Read(DesktopPuppyUiHarness harness,System.Windows.Automation.AutomationElement pet)
    {
        var animation=harness.FindByAutomationId(pet,"PetAnimation");
        using var json=JsonDocument.Parse(animation.Current.HelpText);
        // The diagnostics expose counters only, never titles, paths, or settings.
        Assert.All(json.RootElement.EnumerateObject(),property=>Assert.Equal(JsonValueKind.Number,property.Value.ValueKind));
        return new(json.RootElement.GetProperty("totalTicks").GetInt64(),json.RootElement.GetProperty("maxConsecutiveStationaryTicks").GetInt64());
    }
    private sealed record Counters(long TotalTicks,long MaxConsecutiveStationaryTicks);
}
