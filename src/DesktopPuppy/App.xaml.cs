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
    private UsageCollector? _usageCollector;
    private UsageSqliteStore? _usageStore;
    private Timer? _usageAdvanceTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        StartUsageTracking();

        _petWindow = new PetWindow();
        MainWindow = _petWindow;
        _petWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _usageAdvanceTimer?.Dispose();
        _usageAdvanceTimer = null;
        _usageCollector?.Dispose();
        _usageCollector = null;
        _usageStore?.Dispose();
        _usageStore = null;
        base.OnExit(e);
    }

    private void StartUsageTracking()
    {
        try
        {
            var clock = new SystemClock();
            var watcher = new WinEventForegroundWatcher();
            _usageStore = new UsageSqliteStore();
            _usageStore.Initialize();
            _usageCollector = new UsageCollector(
                Process.GetCurrentProcess().ProcessName,
                clock,
                watcher,
                _usageStore);
            _usageCollector.Start();
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
                new UsageTimerState(_usageCollector, clock),
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(250));
        }
        catch
        {
            _usageAdvanceTimer?.Dispose();
            _usageAdvanceTimer = null;
            _usageCollector?.Dispose();
            _usageCollector = null;
            _usageStore?.Dispose();
            _usageStore = null;
            // Usage tracking is optional at startup; the offline pet remains runnable.
        }
    }

    private sealed record UsageTimerState(UsageCollector Collector, IClock Clock);
}
