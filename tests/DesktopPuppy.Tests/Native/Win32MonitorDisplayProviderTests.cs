using DesktopPuppy.Native;
using Xunit;

namespace DesktopPuppy.Tests.Native;

public sealed class Win32MonitorDisplayProviderTests
{
    // Purpose: Prevent native monitor identifiers from leaking through geometry-validation diagnostics.
    // Coverage: A monitor with a unique device ID and invalid native bounds reaches provider mapping failure.
    // Target effect: The thrown exception and its inner-message chain contain no device identifier.
    [Fact]
    public void Provider_invalid_native_geometry_diagnostic_does_not_expose_device_id()
    {
        const string uniqueDeviceId = "MONITOR-PRIVACY-REGRESSION-7f3c91a2";
        var handle = new IntPtr(44);
        var api = new FakeDisplayApi(new Dictionary<IntPtr, NativeMonitorInfo>
        {
            [handle] = new(uniqueDeviceId,
                new PhysicalRect(0, 0, 0, 1080),
                new PhysicalRect(0, 0, 1920, 1040),
                true)
        }, handle);
        using var events = new FakeDisplaySettingsChangedSource();
        using var provider = new Win32MonitorDisplayProvider(api, events);

        var error = Assert.Throws<InvalidOperationException>(() => provider.GetMonitors());

        Assert.DoesNotContain(uniqueDeviceId, error.ToString(), StringComparison.Ordinal);
    }

    // Purpose: Ensure Windows' enumeration order does not dictate primary selection or discard left-side monitors.
    // Coverage: Fake monitor handles with one primary and one negative-origin secondary.
    // Target effect: Both work areas and the primary flag map correctly.
    [Fact]
    public void Provider_maps_primary_secondary_and_negative_origin_monitors()
    {
        var primaryHandle = new IntPtr(11);
        var secondaryHandle = new IntPtr(22);
        var api = new FakeDisplayApi(new Dictionary<IntPtr, NativeMonitorInfo>
        {
            [secondaryHandle] = new("DISPLAY2",
                new PhysicalRect(-1920, 0, 0, 1080),
                new PhysicalRect(-1920, 0, 0, 1040),
                false),
            [primaryHandle] = new("DISPLAY1",
                new PhysicalRect(0, 0, 1920, 1080),
                new PhysicalRect(0, 0, 1920, 1040),
                true)
        }, secondaryHandle, primaryHandle);
        using var events = new FakeDisplaySettingsChangedSource();
        using var provider = new Win32MonitorDisplayProvider(api, events);

        var monitors = provider.GetMonitors();

        Assert.Equal(2, monitors.Count);
        var secondary = Assert.Single(monitors, monitor => monitor.Bounds.Left == -1920);
        var primary = Assert.Single(monitors, monitor => monitor.IsPrimary);
        Assert.Equal(new PhysicalRect(-1920, 0, 0, 1080), secondary.Bounds);
        Assert.Equal(new PhysicalRect(-1920, 0, 0, 1040), secondary.WorkArea);
        Assert.False(secondary.IsPrimary);
        Assert.Equal(new PhysicalRect(0, 0, 1920, 1040), primary.WorkArea);
        Assert.True(primary.IsPrimary);
        Assert.Equal("DISPLAY1", primary.DeviceId);
    }

    // Purpose: Avoid silently positioning from invented geometry.
    // Coverage: Native enumeration failure and no-primary response.
    // Target effect: A clear failure is returned to the caller; no default rectangle is treated as a monitor.
    [Fact]
    public void Provider_reports_native_enumeration_failure_without_fabricating_a_monitor()
    {
        using var events = new FakeDisplaySettingsChangedSource();
        var failedApi = new FakeDisplayApi(new Dictionary<IntPtr, NativeMonitorInfo>(), enumerationSucceeds: false);
        using (var failedProvider = new Win32MonitorDisplayProvider(failedApi, events))
        {
            var error = Assert.Throws<System.ComponentModel.Win32Exception>(() => failedProvider.GetMonitors());
            Assert.Contains("enumeration", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        var handle = new IntPtr(33);
        var noPrimaryApi = new FakeDisplayApi(new Dictionary<IntPtr, NativeMonitorInfo>
        {
            [handle] = new("DISPLAY3",
                new PhysicalRect(0, 0, 800, 600),
                new PhysicalRect(0, 0, 800, 560),
                false)
        }, handle);
        using var noPrimaryProvider = new Win32MonitorDisplayProvider(noPrimaryApi, events);

        var noPrimaryError = Assert.Throws<InvalidOperationException>(() => noPrimaryProvider.GetMonitors());
        Assert.Contains("primary", noPrimaryError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(noPrimaryProvider.Monitors);
    }

    // Purpose: Avoid retaining windows or receiving callbacks during shutdown.
    // Coverage: Subscribe, dispose, then raise a fake display change.
    // Target effect: No callback reaches a disposed placement client.
    [Fact]
    public void Provider_unsubscribes_display_change_handler_on_dispose()
    {
        var api = new FakeDisplayApi(new Dictionary<IntPtr, NativeMonitorInfo>());
        var events = new FakeDisplaySettingsChangedSource();
        var provider = new Win32MonitorDisplayProvider(api, events);
        var forwarded = 0;
        provider.DisplaySettingsChanged += (_, _) => forwarded++;

        provider.Dispose();
        provider.Dispose();
        events.Raise();

        Assert.Equal(0, forwarded);
        Assert.Equal(0, events.SubscriberCount);
    }


    // Purpose: Keep subscriber failures from escaping the SystemEvents callback boundary.
    // Coverage: A provider subscriber throws while the fake display-settings source raises an event.
    // Target effect: The fake source callback returns normally despite the throwing subscriber.
    [Fact]
    public void Provider_contains_exceptions_from_display_change_subscribers()
    {
        var api = new FakeDisplayApi(new Dictionary<IntPtr, NativeMonitorInfo>());
        var events = new FakeDisplaySettingsChangedSource();
        using var provider = new Win32MonitorDisplayProvider(api, events);
        provider.DisplaySettingsChanged += (_, _) => throw new InvalidOperationException("subscriber failure");

        var escapedException = Record.Exception(events.Raise);

        Assert.Null(escapedException);
    }

    // Purpose: Ensure Dispose drains active forwarding before returning and permanently stops delivery.
    // Coverage: Hold a subscriber on an event gate while disposing, then raise another event after disposal.
    // Target effect: Dispose waits for the active callback; no later event reaches the subscriber.
    [Fact]
    public async Task Provider_dispose_waits_for_in_flight_display_change_and_stops_later_events()
    {
        var api = new FakeDisplayApi(new Dictionary<IntPtr, NativeMonitorInfo>());
        var events = new FakeDisplaySettingsChangedSource();
        using var provider = new Win32MonitorDisplayProvider(api, events);
        using var releaseCallback = new ManualResetEventSlim();
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCount = 0;
        provider.DisplaySettingsChanged += (_, _) =>
        {
            Interlocked.Increment(ref callbackCount);
            callbackEntered.TrySetResult();
            releaseCallback.Wait();
        };

        var callbackTask = Task.Run(events.Raise);
        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var disposeTask = Task.Run(provider.Dispose);
        try
        {
            await events.Unsubscribed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await disposeTask.WaitAsync(TimeSpan.FromMilliseconds(200)));
        }
        finally
        {
            releaseCallback.Set();
        }

        await callbackTask.WaitAsync(TimeSpan.FromSeconds(5));
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
        events.Raise();

        Assert.Equal(1, Volatile.Read(ref callbackCount));
        Assert.Equal(0, events.SubscriberCount);
    }


    // Purpose: Keep reentrant Dispose from deadlocking or starting another subscriber after disposal.
    // Coverage: The first display-change subscriber disposes its provider during notification.
    // Target effect: Dispose returns, the remaining subscriber and all later events are skipped.
    [Fact]
    public async Task Provider_reentrant_dispose_stops_remaining_display_change_subscribers()
    {
        var api = new FakeDisplayApi(new Dictionary<IntPtr, NativeMonitorInfo>());
        var events = new FakeDisplaySettingsChangedSource();
        using var provider = new Win32MonitorDisplayProvider(api, events);
        var firstSubscriberCalls = 0;
        var laterSubscriberCalls = 0;
        provider.DisplaySettingsChanged += (_, _) =>
        {
            Interlocked.Increment(ref firstSubscriberCalls);
            provider.Dispose();
        };
        provider.DisplaySettingsChanged += (_, _) => Interlocked.Increment(ref laterSubscriberCalls);

        await Task.Run(events.Raise).WaitAsync(TimeSpan.FromSeconds(5));
        events.Raise();

        Assert.Equal(1, Volatile.Read(ref firstSubscriberCalls));
        Assert.Equal(0, Volatile.Read(ref laterSubscriberCalls));
        Assert.Equal(0, events.SubscriberCount);
    }
    private sealed class FakeDisplayApi(
        IReadOnlyDictionary<IntPtr, NativeMonitorInfo> monitors,
        params IntPtr[] enumerationOrder) : IWin32DisplayApi
    {
        private readonly IntPtr[] _enumerationOrder = enumerationOrder.Length == 0
            ? monitors.Keys.ToArray()
            : enumerationOrder;

        public FakeDisplayApi(
            IReadOnlyDictionary<IntPtr, NativeMonitorInfo> monitors,
            bool enumerationSucceeds) : this(monitors)
        {
            EnumerationSucceeds = enumerationSucceeds;
        }

        public bool EnumerationSucceeds { get; private set; } = true;

        public bool EnumerateMonitors(Func<IntPtr, bool> callback)
        {
            foreach (var handle in _enumerationOrder)
            {
                if (!callback(handle))
                {
                    break;
                }
            }

            return EnumerationSucceeds;
        }

        public bool TryGetMonitorInfo(IntPtr monitorHandle, out NativeMonitorInfo info) =>
            monitors.TryGetValue(monitorHandle, out info);
    }

    private sealed class FakeDisplaySettingsChangedSource : IDisplaySettingsChangedSource, IDisposable
    {
        private readonly object _gate = new();
        private EventHandler? _displaySettingsChanged;

        public TaskCompletionSource Unsubscribed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler? DisplaySettingsChanged
        {
            add
            {
                lock (_gate)
                {
                    _displaySettingsChanged += value;
                }
            }
            remove
            {
                lock (_gate)
                {
                    _displaySettingsChanged -= value;
                }
                Unsubscribed.TrySetResult();
            }
        }

        public int SubscriberCount
        {
            get
            {
                lock (_gate)
                {
                    return _displaySettingsChanged?.GetInvocationList().Length ?? 0;
                }
            }
        }

        public void Raise()
        {
            EventHandler? handler;
            lock (_gate)
            {
                handler = _displaySettingsChanged;
            }
            handler?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _displaySettingsChanged = null;
            }
        }
    }
}
