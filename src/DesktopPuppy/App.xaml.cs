using System.Diagnostics;
using System.Threading;
using System.Windows;
using DesktopPuppy.Core.Clock;
using DesktopPuppy.Native;
using DesktopPuppy.Pet;
using DesktopPuppy.Usage;

namespace DesktopPuppy;

public partial class App : Application
{
    private PetWindow? _petWindow;
    private ISingleInstanceGuard? _singleInstanceGuard;
    private UsageCollector? _usageCollector;
    private UsageSqliteStore? _usageStore;
    private IForegroundWindowWatcher? _foregroundWatcher;
    private SystemEventsSessionPowerEventSource? _sessionPowerEventSource;
    private Timer? _usageAdvanceTimer;
    private Timer? _usageRetentionCheckTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _singleInstanceGuard = new SingleInstanceGuard();
        if (!_singleInstanceGuard.TryAcquire(ActivateExistingInstance))
        {
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        StartUsageTracking();

        _petWindow = new PetWindow();
        MainWindow = _petWindow;
        _petWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _sessionPowerEventSource?.Dispose();
        _sessionPowerEventSource = null;
        _usageRetentionCheckTimer?.Dispose();
        _usageRetentionCheckTimer = null;
        _usageAdvanceTimer?.Dispose();
        _usageAdvanceTimer = null;
        _usageCollector?.Dispose();
        _usageCollector = null;
        _foregroundWatcher = null;
        _usageStore?.Dispose();
        _usageStore = null;
        _singleInstanceGuard?.Dispose();
        _singleInstanceGuard = null;
        base.OnExit(e);
    }

    private void StartUsageTracking()
    {
        var watcher = new WinEventForegroundWatcher();
        SystemEventsSessionPowerEventSource? sessionPowerEventSource = null;
        try
        {
            var clock = new SystemClock();
            _foregroundWatcher = watcher;
            _usageStore = new UsageSqliteStore();
            _usageStore.Initialize();

            var retentionCoordinator = new UsageRetentionCoordinator(
                _usageStore.PruneBefore,
                TimeZoneInfo.Local);
            TryCheckUsageRetention(retentionCoordinator, clock);
            _usageRetentionCheckTimer = new Timer(
                static state =>
                {
                    var runtime = (UsageRetentionTimerState)state!;
                    TryCheckUsageRetention(runtime.Coordinator, runtime.Clock);
                },
                new UsageRetentionTimerState(retentionCoordinator, clock),
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1));

            var collector = new UsageCollector(
                Process.GetCurrentProcess().ProcessName,
                clock,
                watcher,
                _usageStore);
            _usageCollector = collector;
            collector.Start();
            _usageAdvanceTimer = new Timer(
                static state =>
                {
                    var runtime = (UsageTimerState)state!;
                    try
                    {
                        runtime.Collector.Advance(runtime.Clock.UtcNow);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Application shutdown can race with the final timer callback.
                    }
                },
                new UsageTimerState(collector, clock),
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(250));

            var router = new SessionPowerEventRouter(
                (boundary, at) => collector.OnSessionBoundary(boundary, at),
                collector.RefreshForegroundWindow,
                () => TryCheckUsageRetention(retentionCoordinator, clock));
            sessionPowerEventSource = new SystemEventsSessionPowerEventSource(clock);
            sessionPowerEventSource.Start(router.OnEvent);
            _sessionPowerEventSource = sessionPowerEventSource;
        }
        catch
        {
            sessionPowerEventSource?.Dispose();
            _sessionPowerEventSource = null;
            _usageRetentionCheckTimer?.Dispose();
            _usageRetentionCheckTimer = null;
            _usageAdvanceTimer?.Dispose();
            _usageAdvanceTimer = null;
            if (_usageCollector is not null)
            {
                _usageCollector.Dispose();
            }
            else
            {
                watcher.Dispose();
            }

            _usageCollector = null;
            _foregroundWatcher = null;
            _usageStore?.Dispose();
            _usageStore = null;
            // Usage tracking is optional at startup; the offline pet remains runnable.
        }
    }

    private static void TryCheckUsageRetention(UsageRetentionCoordinator coordinator, IClock clock)
    {
        try
        {
            coordinator.CheckAndPrune(clock.UtcNow);
        }
        catch (ObjectDisposedException)
        {
            // Application shutdown can race with a timer callback.
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Usage history cleanup failed: {exception}");
            // Cleanup is best-effort; the coordinator leaves this local date due for retry.
        }
    }

    private void ActivateExistingInstance()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_petWindow is null)
            {
                return;
            }

            if (!_petWindow.IsVisible)
            {
                _petWindow.Show();
            }

            if (_petWindow.WindowState == WindowState.Minimized)
            {
                _petWindow.WindowState = WindowState.Normal;
            }

            _petWindow.Activate();
        });
    }

    private sealed record UsageTimerState(UsageCollector Collector, IClock Clock);

    private sealed record UsageRetentionTimerState(UsageRetentionCoordinator Coordinator, IClock Clock);
}
