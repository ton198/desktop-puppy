using DesktopPuppy.Usage;
using DesktopPuppy.WindowsAcceptance;
using DesktopPuppy.WindowsAcceptance.Core;
using Xunit;

namespace DesktopPuppy.Tests.WindowsAcceptance;

public sealed class AcceptanceRunTests
{
    [Fact]
    public void Observer_records_only_supported_event_kinds_and_timestamps()
    {
        var observer = new AcceptanceEventRecorder();
        var at = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        observer.Record(SessionPowerEvent.Locked, at);
        observer.Record(SessionPowerEvent.Shutdown, at.AddSeconds(1));
        observer.Record(SessionPowerEvent.Resume, at.AddSeconds(2));

        Assert.Equal(new[] {
            new ObservedSystemEvent(ObservedSystemEventKind.Locked, at),
            new ObservedSystemEvent(ObservedSystemEventKind.Resume, at.AddSeconds(2))
        }, observer.Snapshot());
    }

    [Fact]
    public void Unimplemented_scenarios_cannot_report_passed_from_events_alone()
    {
        var observer = new AcceptanceEventRecorder();
        var at = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        observer.Record(SessionPowerEvent.Locked, at);
        observer.Record(SessionPowerEvent.Unlocked, at.AddSeconds(1));
        Assert.Equal(AcceptanceStatus.Incomplete, observer.NotImplementedResult().Status);
        Assert.Equal(2, observer.NotImplementedResult().ObservedEvents.Count);
    }
}
