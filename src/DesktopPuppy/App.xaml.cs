using System.Diagnostics;
using System.Threading;
using System.Windows;
using DesktopPuppy.Core.Clock;
using DesktopPuppy.Native;
using DesktopPuppy.Pet;
using DesktopPuppy.Pet.Placement;
using DesktopPuppy.Pet.Statistics;
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
    private IUsageStatisticsQuery? _usageStatisticsQuery;
    private UsageStatisticsWindow? _usageStatisticsWindow;
    private Win32MonitorDisplayProvider? _monitorDisplayProvider;
    private PetWindowPlacementService? _petWindowPlacementService;

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

        InitializePetWindowPlacement();
        _petWindow = new PetWindow(_petWindowPlacementService, _monitorDisplayProvider);
        _petWindow.StatisticsRequested += PetWindow_OnStatisticsRequested;
        MainWindow = _petWindow;
        _petWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_petWindow is not null)
        {
            _petWindow.StatisticsRequested -= PetWindow_OnStatisticsRequested;
        }

        _usageStatisticsWindow = null;
        _usageStatisticsQuery = null;
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
        try
        {
            _petWindowPlacementService?.Dispose();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Pet placement service cleanup failed: {exception}");
        }
        finally
        {
            _petWindowPlacementService = null;
        }

        try
        {
            _monitorDisplayProvider?.Dispose();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Monitor display provider cleanup failed: {exception}");
        }
        finally
        {
            _monitorDisplayProvider = null;
        }

        base.OnExit(e);
    }

    private void InitializePetWindowPlacement()
    {
        Win32MonitorDisplayProvider? monitors = null;
        try
        {
            monitors = new Win32MonitorDisplayProvider();
            var nativeWindows = new Win32WindowPositioner(new Win32WindowApi(), monitors);
            var windows = new InitialPrimaryMonitorPositioner(monitors, nativeWindows);
            var placement = new PetWindowPlacementService(monitors, windows);
            _monitorDisplayProvider = monitors;
            _petWindowPlacementService = placement;
        }
        catch (Exception exception)
        {
            try
            {
                monitors?.Dispose();
            }
            catch (Exception disposeException)
            {
                Debug.WriteLine($"Monitor provider cleanup after initialization failure also failed: {disposeException}");
            }

            Debug.WriteLine($"Native pet placement initialization failed; keeping WPF startup placement: {exception}");
            _monitorDisplayProvider = null;
            _petWindowPlacementService = null;
        }
    }

    private sealed class InitialPrimaryMonitorPositioner(
        IMonitorDisplayProvider monitors,
        INativeWindowPositioner inner) : INativeWindowPositioner
    {
        private bool _initialMonitorRead = true;

        public PhysicalRect GetBounds(IntPtr hwnd) => inner.GetBounds(hwnd);

        public MonitorDisplayInfo GetMonitorForWindow(IntPtr hwnd)
        {
            if (_initialMonitorRead)
            {
                _initialMonitorRead = false;
                return monitors.GetMonitors().First(monitor => monitor.IsPrimary);
            }

            return inner.GetMonitorForWindow(hwnd);
        }

        public uint GetDpi(IntPtr hwnd) => inner.GetDpi(hwnd);

        public void SetBounds(IntPtr hwnd, PhysicalRect bounds) => inner.SetBounds(hwnd, bounds);
    }

    private void StartUsageTracking()
    {
        var watcher = new WinEventForegroundWatcher();
        SystemEventsSessionPowerEventSource? sessionPowerEventSource = null;
        try
        {
            var clock = new SystemClock();
            _foregroundWatcher = watcher;
            _usageStore = new UsageSqliteStore(
                Environment.GetEnvironmentVariable("DESKTOP_PUPPY_USAGE_DB_PATH"));
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
            _usageStatisticsQuery = new UsageStatisticsQueryService(
                _usageStore,
                collector,
                TimeZoneInfo.Local);
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
            _usageStatisticsQuery = null;
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

    private void PetWindow_OnStatisticsRequested(object? sender, EventArgs e)
    {
        if (_usageStatisticsWindow is not null)
        {
            if (_usageStatisticsWindow.WindowState == WindowState.Minimized)
            {
                _usageStatisticsWindow.WindowState = WindowState.Normal;
            }

            _usageStatisticsWindow.Activate();
            return;
        }

        var viewModel = new UsageStatisticsViewModel(
            _usageStatisticsQuery ?? UnavailableUsageStatisticsQuery.Instance,
            TimeZoneInfo.Local,
            () => DateTimeOffset.UtcNow);
        var window = new UsageStatisticsWindow(viewModel);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_usageStatisticsWindow, window))
            {
                _usageStatisticsWindow = null;
            }
        };
        _usageStatisticsWindow = window;
        window.Show();
        window.Activate();
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

    private sealed class UnavailableUsageStatisticsQuery : IUsageStatisticsQuery
    {
        public static UnavailableUsageStatisticsQuery Instance { get; } = new();

        public Task<DailyUsageStatistics> QueryAsync(
            DateOnly localDate,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<DailyUsageStatistics>(
                new InvalidOperationException("使用统计数据当前不可用。"));
        }
    }

    private sealed record UsageTimerState(UsageCollector Collector, IClock Clock);

    private sealed record UsageRetentionTimerState(UsageRetentionCoordinator Coordinator, IClock Clock);
}
