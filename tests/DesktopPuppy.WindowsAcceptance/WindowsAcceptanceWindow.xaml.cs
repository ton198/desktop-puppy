using System.Windows;
using DesktopPuppy.Core.Clock;
using DesktopPuppy.WindowsAcceptance.Core;

namespace DesktopPuppy.WindowsAcceptance;

public partial class WindowsAcceptanceWindow : Window
{
    private string? scenario;
    private AcceptanceRun? run;
    private CancellationTokenSource? startCancellation;
    private CancellationTokenSource? scenarioCancellation;
    private Task<AcceptanceRun>? pendingStartup;
    private Task? pendingScenario;
    private TimeSpan stepTimeout;
    private bool timeoutConfigurationValid;
    private bool closing;

    public WindowsAcceptanceWindow()
    {
        InitializeComponent();
        var prerequisitesPassed = CheckPrerequisites();
        timeoutConfigurationValid = !prerequisitesPassed || AcceptanceStepTimeout.TryRead(
            Environment.GetEnvironmentVariable("DESKTOP_PUPPY_ACCEPTANCE_STEP_TIMEOUT_MINUTES"), out stepTimeout);
        if (prerequisitesPassed && !timeoutConfigurationValid)
        {
            ResultState.Text = AcceptanceStatus.Failed.ToString();
            ResultDetail.Text = "DESKTOP_PUPPY_ACCEPTANCE_STEP_TIMEOUT_MINUTES must be a whole number from 1 to 120.";
            BeginButton.IsEnabled = false;
        }
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
        ScenarioSummary.Text = "E2E-POWER-01 — Real lock/unlock session split.\n\n" +
            "Purpose: Verify actual Windows lock/unlock notifications reach the app and stop counting locked-screen time.\n\n" +
            "Coverage: Foreground fixture A, actual lock/unlock, observer event timestamps, product SQLite session edges, focus transitions A-to-B-to-controller, and separate PIDs for both fixtures.\n\n" +
            "Target effect: Pre-lock fixture-A usage ends near Locked; no fixture row spans the locked interval beyond 5 seconds; fixture A resumes after Unlocked; switching to B closes A and returning to the controller closes B.";
        Instructions.Text = "Focus fixture A before beginning. You alone lock Windows, then unlock and sign in if Windows requires it. After the real Unlocked notification, focus fixture A, switch to fixture B, then return to this controller and confirm. Deadline: " + stepTimeout.TotalMinutes.ToString("0") + " minutes. Cancel at any time.";
        BeginButton.IsEnabled = timeoutConfigurationValid;
        ResultState.Text = timeoutConfigurationValid ? "Not started" : AcceptanceStatus.Failed.ToString();
        ResultDetail.Text = timeoutConfigurationValid
            ? "No lock or unlock action will be initiated by this utility."
            : "Timeout configuration is invalid. Use a whole number from 1 to 120 minutes.";
    }

    private void ChoosePower(object sender, RoutedEventArgs e)
    {
        scenario = "power";
        ScenarioSummary.Text = "Purpose: verify usage-session boundaries across actual suspend/resume notifications.\n\n" +
            "Coverage: operator-initiated sleep/wake and fixture focus transitions, observed system-event timestamps, and persisted session edges within the configured " + stepTimeout.TotalMinutes.ToString("0") + " minute deadline.\n\n" +
            "Target effect: suspend time is excluded and fixture usage resumes only after the real Resume notification.";
        Instructions.Text = "The suspend/resume operator flow is reserved for Task 4 and is not runnable in this Task 3 build; do not sleep or wake the machine for this scenario.";
        BeginButton.IsEnabled = false;
        CompleteButton.IsEnabled = false;
        ResultState.Text = AcceptanceStatus.Incomplete.ToString();
        ResultDetail.Text = "Suspend/resume validation is not implemented in Task 3; no acceptance verdict is available.";
    }

    private async void Begin(object sender, RoutedEventArgs e)
    {
        if (scenario != "lock" || !CheckPrerequisites()) return;
        if (!AcceptanceStepTimeout.TryRead(
                Environment.GetEnvironmentVariable("DESKTOP_PUPPY_ACCEPTANCE_STEP_TIMEOUT_MINUTES"), out stepTimeout))
        {
            ShowResult(AcceptanceStatus.Failed,
                "DESKTOP_PUPPY_ACCEPTANCE_STEP_TIMEOUT_MINUTES must be a whole number from 1 to 120.");
            return;
        }
        BeginButton.IsEnabled = false;
        LockScenario.IsEnabled = false;
        PowerScenario.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ResultState.Text = "Starting isolated lock run";
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
            scenarioCancellation = new CancellationTokenSource();
            ResultState.Text = "Waiting for operator lock/unlock";
            ResultDetail.Text = $"Real Windows event observation is active. The operator alone performs lock/unlock. Deadline: {run.StepTimeout.TotalMinutes:0} minutes.";
            UpdateLockInstructions(run.ObservedEvents);
            pendingScenario = ObserveLockScenarioAsync(run.RunLockUnlockAsync(scenarioCancellation.Token));
        }
        catch (AcceptanceSkippedException ex) { ShowResult(AcceptanceStatus.Skipped, ex.Message); }
        catch (OperationCanceledException) { ShowResult(AcceptanceStatus.Incomplete, "Startup cancelled; no acceptance verdict was produced."); }
        catch (Exception)
        {
            ShowResult(AcceptanceStatus.Failed, "Isolated run could not start or clean up; inspect the local build environment before retrying.");
        }
        finally
        {
            startCancellation?.Dispose();
            startCancellation = null;
            pendingStartup = null;
        }
    }

    private async Task ObserveLockScenarioAsync(Task<AcceptanceResult> scenarioTask)
    {
        try
        {
            var result = await scenarioTask;
            ShowResult(result.Status, result.Summary);
        }
        catch (Exception)
        {
            ShowResult(AcceptanceStatus.Failed, "Lock acceptance could not safely read or validate the owned session data.");
        }
        finally
        {
            scenarioCancellation?.Dispose();
            scenarioCancellation = null;
            await EndRunSafelyAsync();
            pendingScenario = null;
        }
    }

    private void OnEventsChanged(IReadOnlyList<ObservedSystemEvent> events)
    {
        // SystemEvents callbacks do not run on the WPF dispatcher.
        if (closing || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (closing || run is null) return;
            UpdateLockInstructions(events);
        });
    }

    private void UpdateLockInstructions(IReadOnlyList<ObservedSystemEvent> events)
    {
        var lockEvents = events.Where(e => e.Kind is ObservedSystemEventKind.Locked or ObservedSystemEventKind.Unlocked).ToArray();
        if (lockEvents.Length == 0)
        {
            Instructions.Text += "\n\nWaiting for the real Locked notification. Focus fixture A before you lock.";
            return;
        }
        if (lockEvents.Length == 1 && lockEvents[0].Kind == ObservedSystemEventKind.Locked)
        {
            ResultDetail.Text = "A real Locked notification was observed. Unlock Windows and sign in yourself if required. Do not use this utility to unlock.";
            return;
        }
        if (lockEvents.Length == 2 && lockEvents[0].Kind == ObservedSystemEventKind.Locked &&
            lockEvents[1].Kind == ObservedSystemEventKind.Unlocked && lockEvents[0].At < lockEvents[1].At)
        {
            Instructions.Text = "A real Locked then Unlocked notification pair was observed. Now focus fixture A, switch to fixture B to close the resumed A session, then return here and confirm. The button records completion after controller activation.";
            CompleteButton.IsEnabled = true;
            ResultDetail.Text = "Real lock/unlock events observed; waiting for fixture focus transitions and your confirmation.";
        }
        else
        {
            ResultDetail.Text = "Unexpected lock/unlock notification order. This run cannot pass; it will report Failed.";
        }
    }

    private void Complete(object sender, RoutedEventArgs e)
    {
        if (run is null || scenario != "lock") return;
        CompleteButton.IsEnabled = false;
        run.ConfirmLockUnlockCompletion(new SystemClock().UtcNow);
    }

    private async void Cancel(object sender, RoutedEventArgs e)
    {
        startCancellation?.Cancel();
        scenarioCancellation?.Cancel();
        CompleteButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        ResultState.Text = AcceptanceStatus.Incomplete.ToString();
        ResultDetail.Text = "Operator cancelled; no acceptance verdict was produced.";
        if (pendingScenario is null) await EndRunSafelyAsync();
    }

    private void ShowResult(AcceptanceStatus status, string detail)
    {
        if (closing) return;
        ResultState.Text = status.ToString();
        ResultDetail.Text = detail;
        BeginButton.IsEnabled = status != AcceptanceStatus.Skipped && scenario == "lock";
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
        scenarioCancellation?.Cancel();
        try
        {
            if (pendingStartup is not null)
            {
                try { await (await pendingStartup).DisposeAsync(); }
                catch (Exception) { /* Startup failure or cleanup is reported without sensitive details. */ }
            }
            if (pendingScenario is not null)
            {
                try { await pendingScenario; }
                catch (Exception) { /* Scenario cancellation is incomplete, not a passing verdict. */ }
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
