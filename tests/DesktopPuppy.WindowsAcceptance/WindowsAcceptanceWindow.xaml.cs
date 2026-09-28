using System.Windows;
using DesktopPuppy.WindowsAcceptance.Core;

namespace DesktopPuppy.WindowsAcceptance;

public partial class WindowsAcceptanceWindow : Window
{
    private string? scenario;
    private AcceptanceRun? run;
    private CancellationTokenSource? startCancellation;
    private Task<AcceptanceRun>? pendingStartup;
    private bool closing;

    public WindowsAcceptanceWindow()
    {
        InitializeComponent();
        CheckPrerequisites();
    }

    private bool CheckPrerequisites()
    {
        var reason = WindowsAcceptancePrerequisite.SkipReason();
        if (reason is null) return true;
        ResultState.Text = "Skipped";
        ResultDetail.Text = reason;
        BeginButton.IsEnabled = false;
        CompleteButton.IsEnabled = false;
        LockScenario.IsEnabled = false;
        PowerScenario.IsEnabled = false;
        return false;
    }

    private void ChooseLock(object sender, RoutedEventArgs e)
    {
        scenario = "lock";
        Instructions.Text = "Purpose: check real lock/unlock session boundaries. Coverage: with fixture A foreground, manually lock and unlock Windows; focus fixture A again, switch to fixture B, then return to this controller and confirm. Target effect: A closes at lock, then the resumed A session and B session close. Do not start the lock until A is foreground. Task 2 prepares fixtures only; do not perform the transition yet.";
        BeginButton.IsEnabled = true;
        ResultState.Text = "Not started";
        ResultDetail.Text = "Task 2 can launch isolated targets and observe real events; persisted boundary validation is not implemented.";
    }

    private void ChoosePower(object sender, RoutedEventArgs e)
    {
        scenario = "power";
        Instructions.Text = "Purpose: check real suspend/resume session boundaries. Coverage: with fixture A foreground, manually sleep and wake Windows, optionally unlock, focus fixture A, switch to fixture B, then return to this controller and confirm. Target effect: A closes at suspend; B closes the resumed session. Do not start sleep until A is foreground. Task 2 prepares fixtures only; do not perform the transition yet.";
        BeginButton.IsEnabled = true;
        ResultState.Text = "Not started";
        ResultDetail.Text = "Task 2 can launch isolated targets and observe real events; persisted boundary validation is not implemented.";
    }

    private async void Begin(object sender, RoutedEventArgs e)
    {
        if (scenario is null || !CheckPrerequisites()) return;
        BeginButton.IsEnabled = false;
        LockScenario.IsEnabled = false;
        PowerScenario.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ResultState.Text = "Starting isolated run";
        ResultDetail.Text = "Preparing the isolated product and two fixture windows. No Windows transition is initiated.";
        startCancellation = new CancellationTokenSource();
        try
        {
            pendingStartup = Task.Run(() => AcceptanceRun.StartAsync(
                Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE")!, startCancellation.Token));
            var started = await pendingStartup;
            if (closing || startCancellation.IsCancellationRequested)
            {
                await started.DisposeAsync();
                return;
            }
            run = started;
            run.EventsChanged += OnEventsChanged;
            ResultState.Text = "Waiting for operator";
            ResultDetail.Text = "Isolated product and fixture A/B are running. Real event observation is active; no scenario can pass until Tasks 3/4 implement manual steps and persisted boundary checks. Do not lock or sleep for this Task 2 run.";
            CompleteButton.IsEnabled = true;
        }
        catch (AcceptanceSkippedException ex) { ShowResult(AcceptanceStatus.Skipped, ex.Message); }
        catch (OperationCanceledException) { ShowResult(AcceptanceStatus.Incomplete, "Startup cancelled; no acceptance verdict was produced."); }
        catch (Exception) { ShowResult(AcceptanceStatus.Failed, "Isolated run could not start or clean up; inspect the local build environment before retrying."); }
        finally
        {
            startCancellation?.Dispose();
            startCancellation = null;
            pendingStartup = null;
        }
    }

    private void OnEventsChanged(IReadOnlyList<ObservedSystemEvent> events)
    {
        // SystemEvents callbacks do not run on the WPF dispatcher.
        if (closing || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (closing || run is null) return;
            ResultDetail.Text = $"Real system events observed: {events.Count}. No scenario validation is implemented in Task 2.";
        });
    }

    private async void Complete(object sender, RoutedEventArgs e)
    {
        if (run is null) return;
        CompleteButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        try
        {
            var result = scenario == "lock"
                ? await run.RunLockUnlockAsync(CancellationToken.None)
                : await run.RunSuspendResumeAsync(CancellationToken.None);
            ShowResult(result.Status, result.Summary);
        }
        finally { await EndRunSafelyAsync(); }
    }

    private async void Cancel(object sender, RoutedEventArgs e)
    {
        startCancellation?.Cancel();
        CompleteButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        ShowResult(AcceptanceStatus.Incomplete, "Operator cancelled; no acceptance verdict was produced.");
        await EndRunSafelyAsync();
    }

    private void ShowResult(AcceptanceStatus status, string detail)
    {
        if (closing) return;
        ResultState.Text = status.ToString();
        ResultDetail.Text = detail;
        BeginButton.IsEnabled = status != AcceptanceStatus.Skipped;
        CompleteButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        LockScenario.IsEnabled = status != AcceptanceStatus.Skipped;
        PowerScenario.IsEnabled = status != AcceptanceStatus.Skipped;
    }

    private async Task EndRunAsync()
    {
        var ending = run;
        run = null;
        if (ending is null) return;
        ending.EventsChanged -= OnEventsChanged;
        await ending.DisposeAsync();
    }

    private async Task EndRunSafelyAsync()
    {
        try { await EndRunAsync(); }
        catch (Exception)
        {
            ShowResult(AcceptanceStatus.Failed,
                "An owned child could not be fully cleaned up; the run directory was retained for safety.");
        }
    }

    protected override async void OnClosed(EventArgs e)
    {
        closing = true;
        startCancellation?.Cancel();
        try
        {
            if (pendingStartup is not null)
            {
                try { await (await pendingStartup).DisposeAsync(); }
                catch (Exception) { /* Startup failure or cleanup is reported without sensitive details. */ }
            }
            await EndRunSafelyAsync();
        }
        finally
        {
            base.OnClosed(e);
            Application.Current.Shutdown();
        }
    }
}
