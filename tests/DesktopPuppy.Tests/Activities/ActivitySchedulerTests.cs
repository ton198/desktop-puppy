using DesktopPuppy.Activities;
using Xunit;
namespace DesktopPuppy.Tests.Activities;
public sealed class ActivitySchedulerTests
{
    [Fact] public void Trigger_cooldowns_survive_restart_and_block_exactly_until_boundary()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".db");
        try {
            var at = new DateTimeOffset(2026,10,9,12,0,0,TimeSpan.Zero);
            var scheduler = new ActivityScheduler(path, new Random(1));
            var context = new ActivityContext(at, TimeZoneInfo.Utc);
            var decision = Assert.IsType<ActivityDecision>(scheduler.Evaluate(context));
            scheduler.MarkTriggered(decision, at);
            Assert.False(scheduler.CanTriggerGlobal(context));
            scheduler = new ActivityScheduler(path, new Random(1));
            Assert.Null(scheduler.Evaluate(context with { AtUtc=at.AddMinutes(19) }));
            Assert.NotNull(scheduler.Evaluate(context with { AtUtc=at.AddMinutes(20) }));
            Assert.Null(scheduler.Evaluate(context with { AtUtc=at.AddMinutes(90), Paused=true }));
            Assert.Null(scheduler.Evaluate(context with { AtUtc=at.AddHours(12) }));
        } finally { File.Delete(path); }
    }
    [Fact] public void Blocked_greeting_does_not_consume_day_and_trigger_is_persistent()
    {
        var path=Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".db");
        try {
            var date=new DateOnly(2026,10,9); var greeting=new GreetingService(path);
            Assert.False(greeting.CanTrigger(date,false)); Assert.True(greeting.CanTrigger(date,true));
            greeting.MarkTriggered(date, DateTimeOffset.UtcNow);
            Assert.False(new GreetingService(path).CanTrigger(date,true));
            Assert.True(greeting.CanTrigger(date.AddDays(1),true));
        } finally { File.Delete(path); }
    }
}
