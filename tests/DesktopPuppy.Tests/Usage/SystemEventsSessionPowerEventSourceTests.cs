using DesktopPuppy.Core.Clock;
using DesktopPuppy.Usage;
using Microsoft.Win32;
using Xunit;

namespace DesktopPuppy.Tests.Usage;

public sealed class SystemEventsSessionPowerEventSourceTests
{
    private static readonly DateTimeOffset EventTime = new(2026, 9, 24, 16, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(SessionSwitchReason.SessionLock, SessionPowerEvent.Locked)]
    [InlineData(SessionSwitchReason.SessionUnlock, SessionPowerEvent.Unlocked)]
    public void Session_lock_and_unlock_notifications_are_normalized_with_the_current_time(
        SessionSwitchReason reason,
        SessionPowerEvent expectedEvent)
    {
        var provider = new FakeSessionPowerNotificationProvider();
        var clock = new FixedClock(EventTime);
        var events = new List<(SessionPowerEvent Event, DateTimeOffset At)>();
        using var source = new SystemEventsSessionPowerEventSource(provider, clock);
        source.Start((sessionEvent, at) => events.Add((sessionEvent, at)));

        provider.RaiseSessionSwitch(reason);

        Assert.Equal((expectedEvent, EventTime), Assert.Single(events));
    }

    [Fact]
    public void Unrelated_session_switch_reasons_do_not_emit_usage_events()
    {
        var provider = new FakeSessionPowerNotificationProvider();
        var events = new List<(SessionPowerEvent Event, DateTimeOffset At)>();
        using var source = new SystemEventsSessionPowerEventSource(provider, new FixedClock(EventTime));
        source.Start((sessionEvent, at) => events.Add((sessionEvent, at)));

        provider.RaiseSessionSwitch(SessionSwitchReason.ConsoleConnect);

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(PowerModes.Suspend, SessionPowerEvent.Suspend)]
    [InlineData(PowerModes.Resume, SessionPowerEvent.Resume)]
    public void Suspend_and_resume_notifications_are_normalized(
        PowerModes powerMode,
        SessionPowerEvent expectedEvent)
    {
        var provider = new FakeSessionPowerNotificationProvider();
        var events = new List<(SessionPowerEvent Event, DateTimeOffset At)>();
        using var source = new SystemEventsSessionPowerEventSource(provider, new FixedClock(EventTime));
        source.Start((sessionEvent, at) => events.Add((sessionEvent, at)));

        provider.RaisePowerModeChanged(powerMode);

        Assert.Equal((expectedEvent, EventTime), Assert.Single(events));
    }

    [Fact]
    public void Power_status_change_does_not_end_or_restart_a_foreground_session()
    {
        var provider = new FakeSessionPowerNotificationProvider();
        var events = new List<(SessionPowerEvent Event, DateTimeOffset At)>();
        using var source = new SystemEventsSessionPowerEventSource(provider, new FixedClock(EventTime));
        source.Start((sessionEvent, at) => events.Add((sessionEvent, at)));

        provider.RaisePowerModeChanged(PowerModes.StatusChange);

        Assert.Empty(events);
    }

    [Fact]
    public void Disposing_the_source_unsubscribes_it_from_later_system_notifications()
    {
        var provider = new FakeSessionPowerNotificationProvider();
        var events = new List<(SessionPowerEvent Event, DateTimeOffset At)>();
        var source = new SystemEventsSessionPowerEventSource(provider, new FixedClock(EventTime));
        source.Start((sessionEvent, at) => events.Add((sessionEvent, at)));
        source.Dispose();

        provider.RaiseSessionSwitch(SessionSwitchReason.SessionLock);
        provider.RaisePowerModeChanged(PowerModes.Suspend);

        Assert.Empty(events);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakeSessionPowerNotificationProvider : ISessionPowerNotificationProvider
    {
        public event SessionSwitchEventHandler? SessionSwitch;

        public event PowerModeChangedEventHandler? PowerModeChanged;

        public void RaiseSessionSwitch(SessionSwitchReason reason) =>
            SessionSwitch?.Invoke(this, new SessionSwitchEventArgs(reason));

        public void RaisePowerModeChanged(PowerModes mode) =>
            PowerModeChanged?.Invoke(this, new PowerModeChangedEventArgs(mode));
    }
}
