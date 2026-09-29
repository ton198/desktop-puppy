using DesktopPuppy.Usage;
using DesktopPuppy.WindowsAcceptance;
using DesktopPuppy.WindowsAcceptance.Core;
using Xunit;

namespace DesktopPuppy.Tests.WindowsAcceptance;

public sealed class AcceptanceRunTests
{
    // Purpose: Ensure the observer records only the system-event kinds accepted by the power-session contract.
    // Coverage: Record a supported lock event, an unsupported shutdown event, and a supported resume event with timestamps.
    // Target effect: Unsupported event kinds are excluded while supported kinds retain their original timestamps.
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

    // Purpose: Prevent event observations alone from passing an acceptance scenario without implemented checks.
    // Coverage: Record lock/unlock events and evaluate the explicitly unimplemented scenario result.
    // Target effect: The result remains Incomplete and retains the observed event count.
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
