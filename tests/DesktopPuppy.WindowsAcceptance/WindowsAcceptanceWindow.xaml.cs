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
        BeginButton.Content = "Begin isolated lock scenario";
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
        ScenarioSummary.Text = "E2E-POWER-02 — Real suspend/resume session split.\n\n" +
            "Purpose: verify actual Windows Suspend/Resume notifications reach the app and collection restarts after the machine resumes.\n\n" +
            "Coverage: pre-suspend fixture A, operator-initiated system suspend/resume, optional manual unlock, post-resume A-to-B-to-controller transitions, and isolated SQLite session boundaries for both fixture PIDs.\n\n" +
            "Target effect: fixture sessions respect the 15-second Suspend/Resume and completion boundaries. The result reports generic Suspend/Resume and does not distinguish sleep from hibernate.";
        Instructions.Text = "Save unrelated work first. Focus fixture A, then begin. You alone manually put Windows to sleep and wake it; unlock/sign in if requested. After Resume, focus fixture A, switch to fixture B, then return here and confirm. The awake-time deadline pauses between observed Suspend and Resume. No system transition is initiated by this utility.";
        BeginButton.Content = "Begin isolated suspend/resume scenario";
        BeginButton.IsEnabled = timeoutConfigurationValid;
        CompleteButton.Content = "Confirm fixture switches complete";
        CompleteButton.IsEnabled = false;
        ResultState.Text = timeoutConfigurationValid ? "Not started" : AcceptanceStatus.Failed.ToString();
        ResultDetail.Text = timeoutConfigurationValid
            ? "No suspend, resume, unlock, or input action will be initiated by this utility."
            : "Timeout configuration is invalid. Use a whole number from 1 to 120 minutes.";
    }
    private async void Begin(object sender, RoutedEventArgs e)
    {
        if (scenario is not ("lock" or "power") || !CheckPrerequisites()) return;
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
        ResultState.Text = scenario == "lock" ? "Starting isolated lock run" : "Starting isolated suspend/resume run";
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
            ResultState.Text = scenario == "lock" ? "Waiting for operator lock/unlock" : "Waiting for operator Suspend/Resume";
            ResultDetail.Text = scenario == "lock"
                ? $"Real Windows event observation is active. The operator alone performs lock/unlock. Deadline: {run.StepTimeout.TotalMinutes:0} minutes."
                : $"Real Windows event observation is active. The operator alone suspends and wakes Windows. Awake-time deadline: {run.StepTimeout.TotalMinutes:0} minutes.";
            UpdateScenarioInstructions(run.ObservedEvents);
            pendingScenario = scenario == "lock"
                ? ObserveLockScenarioAsync(run.RunLockUnlockAsync(scenarioCancellation.Token))
                : ObservePowerScenarioAsync(run.RunSuspendResumeAsync(scenarioCancellation.Token));
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

    private async Task ObservePowerScenarioAsync(Task<AcceptanceResult> scenarioTask)
    {
        try
        {
            var result = await scenarioTask;
            ShowResult(result.Status, result.Summary);
        }
        catch (Exception)
        {
            ShowResult(AcceptanceStatus.Failed, "Suspend/resume acceptance could not safely read or validate the owned session data.");
        }
        finally
        {
            scenarioCancellation?.Dispose();
            scenarioCancellation = null;
            await EndRunSafelyAsync();
            pendingScenario = null;
        }
    }

    private void UpdateScenarioInstructions(IReadOnlyList<ObservedSystemEvent> events)
    {
        if (scenario == "power") UpdatePowerInstructions(events);
        else UpdateLockInstructions(events);
    }

    private void UpdatePowerInstructions(IReadOnlyList<ObservedSystemEvent> events)
    {
        var powerEvents = events.Where(e => e.Kind is ObservedSystemEventKind.Suspend or ObservedSystemEventKind.Resume).ToArray();
        if (powerEvents.Length == 0)
        {
            Instructions.Text = "Save unrelated work. The operator alone manually puts Windows to sleep. Focus fixture A before suspending; awake-time deadline: " +
                stepTimeout.TotalMinutes.ToString("0") + " minutes (paused while suspended).";
            return;
        }
        if (powerEvents.Length == 1 && powerEvents[0].Kind == ObservedSystemEventKind.Suspend)
        {
            ResultDetail.Text = "A real Suspend notification was observed. Wake Windows yourself. The awake-time deadline remains paused until Resume.";
            return;
        }
        if (powerEvents.Length == 2 && powerEvents[0].Kind == ObservedSystemEventKind.Suspend &&
            powerEvents[1].Kind == ObservedSystemEventKind.Resume && powerEvents[0].At < powerEvents[1].At)
        {
            Instructions.Text = "A real Suspend then Resume notification pair was observed. Unlock/sign in yourself if requested, focus fixture A, switch to fixture B to close A's resumed session, then return here and confirm.";
            CompleteButton.IsEnabled = true;
            ResultDetail.Text = "Real Suspend/Resume events observed; waiting for fixture focus transitions and your confirmation.";
        }
        else
        {
            ResultDetail.Text = "Unexpected Suspend/Resume notification order. This run cannot pass.";
        }
    }
    private void OnEventsChanged(IReadOnlyList<ObservedSystemEvent> events)
    {
        // SystemEvents callbacks do not run on the WPF dispatcher.
        if (closing || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (closing || run is null) return;
            UpdateScenarioInstructions(events);
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
        if (run is null) return;
        CompleteButton.IsEnabled = false;
        if (scenario == "lock") run.ConfirmLockUnlockCompletion(new SystemClock().UtcNow);
        else if (scenario == "power") run.ConfirmSuspendResumeCompletion(new SystemClock().UtcNow);
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
        BeginButton.IsEnabled = status != AcceptanceStatus.Skipped && scenario is "lock" or "power";
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
