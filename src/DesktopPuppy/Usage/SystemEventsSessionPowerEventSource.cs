using DesktopPuppy.Core.Clock;
using Microsoft.Win32;

namespace DesktopPuppy.Usage;

public interface ISessionPowerNotificationProvider
{
    event SessionSwitchEventHandler? SessionSwitch;

    event PowerModeChangedEventHandler? PowerModeChanged;
}

public sealed class SystemEventsSessionPowerEventSource : IDisposable
{
    private readonly object _gate = new();
    private readonly ISessionPowerNotificationProvider _provider;
    private readonly IClock _clock;
    private Action<SessionPowerEvent, DateTimeOffset>? _onEvent;
    private bool _started;
    private bool _disposed;

    public SystemEventsSessionPowerEventSource(IClock clock)
        : this(new SystemEventsNotificationProvider(), clock)
    {
    }

    public SystemEventsSessionPowerEventSource(
        ISessionPowerNotificationProvider provider,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(clock);
        _provider = provider;
        _clock = clock;
    }

    public void Start(Action<SessionPowerEvent, DateTimeOffset> onEvent)
    {
        ArgumentNullException.ThrowIfNull(onEvent);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                throw new InvalidOperationException("The session/power event source is already started.");
            }

            _onEvent = onEvent;
            var sessionSwitchSubscribed = false;
            try
            {
                _provider.SessionSwitch += OnSessionSwitch;
                sessionSwitchSubscribed = true;
                _provider.PowerModeChanged += OnPowerModeChanged;
                _started = true;
            }
            catch
            {
                if (sessionSwitchSubscribed)
                {
                    _provider.SessionSwitch -= OnSessionSwitch;
                }

                _onEvent = null;
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_started)
            {
                _provider.SessionSwitch -= OnSessionSwitch;
                _provider.PowerModeChanged -= OnPowerModeChanged;
            }

            _started = false;
            _onEvent = null;
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs args)
    {
        var sessionEvent = args.Reason switch
        {
            SessionSwitchReason.SessionLock => SessionPowerEvent.Locked,
            SessionSwitchReason.SessionUnlock => SessionPowerEvent.Unlocked,
            _ => (SessionPowerEvent?)null
        };
        Dispatch(sessionEvent);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs args)
    {
        var sessionEvent = args.Mode switch
        {
            PowerModes.Suspend => SessionPowerEvent.Suspend,
            PowerModes.Resume => SessionPowerEvent.Resume,
            _ => (SessionPowerEvent?)null
        };
        Dispatch(sessionEvent);
    }

    private void Dispatch(SessionPowerEvent? sessionEvent)
    {
        if (sessionEvent is null)
        {
            return;
        }

        lock (_gate)
        {
            if (!_started || _disposed || _onEvent is null)
            {
                return;
            }

            try
            {
                _onEvent(sessionEvent.Value, _clock.UtcNow);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError("Failed to process a session/power event: {0}", exception);
            }
        }
    }

    private sealed class SystemEventsNotificationProvider : ISessionPowerNotificationProvider
    {
        public event SessionSwitchEventHandler? SessionSwitch
        {
            add => SystemEvents.SessionSwitch += value;
            remove => SystemEvents.SessionSwitch -= value;
        }

        public event PowerModeChangedEventHandler? PowerModeChanged
        {
            add => SystemEvents.PowerModeChanged += value;
            remove => SystemEvents.PowerModeChanged -= value;
        }
    }
}
