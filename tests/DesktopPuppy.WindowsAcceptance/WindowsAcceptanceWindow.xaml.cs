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
    private Task<DisplayAcceptanceRun>? pendingDisplayStartup;
    private Task? pendingScenario;
    private DisplayAcceptanceRun? displayRun;
    private DisplayAcceptanceScenario? displayScenario;
    private IReadOnlyList<DisplayAcceptanceMonitor>? displayMonitors;
    private readonly List<DisplayAcceptanceObservation> displayObservations = [];
    private IntPtr displayPetHwnd;
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
        CaptureDisplayButton.IsEnabled = false;
        DisplayRemovalScenarioButton.IsEnabled = false;
        return false;
    }

    private void ChooseLock(object sender, RoutedEventArgs e)
    {
        DisposeDisplayScenario();
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
        DisposeDisplayScenario();
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
    private void ChooseDisplay(object sender, RoutedEventArgs e)
    {
        DisposeDisplayScenario();
        scenario = "display";
        displayScenario = new DisplayAcceptanceScenario(new ReadOnlyDisplayAcceptanceNativeApi());
        displayObservations.Clear();
        displayPetHwnd = IntPtr.Zero;
        displayMonitors = null;
        BeginButton.Content = "Begin isolated mixed-DPI display scenario";
        CompleteButton.IsEnabled = false;
        CaptureDisplayButton.IsEnabled = false;
        ScenarioSummary.Text = "E2E-DISPLAY-01 — Mixed DPI placement.\n\n" +
            "Purpose: Prove the real WPF pet placement path uses actual display geometry and DPI conversion rather than only pure policy tests.\n\n" +
            "Coverage: At least two connected displays; the same Release app HWND manually dragged by the operator to each display; read-only GetDpiForWindow, physical bounds, containing display, work area, and edge-clamp observations.\n\n" +
            "Target effect: Distinct DPI values are observed on the same HWND; the pet remains on each requested display with DPI-scaled physical bounds and at least 20 visible pixels. Equal measured DPI values report Skipped with the measured reason.";
        Instructions.Text = "The utility first checks both opt-in flags, a local interactive session, and at least two connected displays before creating a run database or starting Desktop Puppy. After Begin, manually drag the same pet window near an edge on each named display, let WPF placement settle, then use the matching confirmation button to capture read-only UIA/native geometry. The utility never injects input, calls SetWindowPos, or changes display settings.";

        var skipReason = GetDisplayPrerequisiteSkipReason();
        if (skipReason is not null)
        {
            ShowResult(AcceptanceStatus.Skipped, skipReason);
            DisposeDisplayScenario();
            return;
        }

        ResultState.Text = "Not started";
        ResultDetail.Text = "Display prerequisites passed. No product process or run database has been created.";
        BeginButton.IsEnabled = timeoutConfigurationValid;
        LockScenario.IsEnabled = true;
        PowerScenario.IsEnabled = true;
        DisplayScenarioButton.IsEnabled = true;
    }

    private void ChooseDisplayRemoval(object sender, RoutedEventArgs e)
    {
        DisposeDisplayScenario();
        scenario = "display-removal";
        displayScenario = new DisplayAcceptanceScenario(new ReadOnlyDisplayAcceptanceNativeApi());
        displayObservations.Clear();
        displayPetHwnd = IntPtr.Zero;
        displayMonitors = null;
        BeginButton.Content = "Begin isolated monitor-removal scenario";
        CompleteButton.IsEnabled = false;
        CaptureDisplayButton.IsEnabled = false;
        ScenarioSummary.Text = "E2E-DISPLAY-02 — Monitor removal recovery.\n\n" +
            "Purpose: Prove the app re-evaluates a pet window whose display disappears and applies the documented primary fallback.\n\n" +
            "Coverage: Pet on secondary display; operator manually disconnects/disables it; actual display-change event; refreshed monitor enumeration; captured pre-change request compared with primary-work-area policy result.\n\n" +
            "Target effect: Actual HWND bounds match the expected fallback within 1 physical pixel, remain at least 20 physical pixels visible, and the app remains running. Visibility alone does not pass.";
        Instructions.Text = "Requires DESKTOP_PUPPY_WINDOWS_ACCEPTANCE=1, DESKTOP_PUPPY_DISPLAY_ACCEPTANCE=1, a local interactive Windows desktop, and at least two connected displays. After Begin, manually move the real isolated pet to Display 2 and capture its pre-removal placement. Then manually disconnect or disable only Display 2 using Windows controls. The utility never changes topology or automatically restores the display.";

        var skipReason = GetDisplayPrerequisiteSkipReason();
        if (skipReason is not null)
        {
            ShowResult(AcceptanceStatus.Skipped, skipReason);
            DisposeDisplayScenario();
            return;
        }

        ResultState.Text = "Not started";
        ResultDetail.Text = "Display-removal prerequisites passed. No product process or run database has been created.";
        BeginButton.IsEnabled = timeoutConfigurationValid;
        LockScenario.IsEnabled = true;
        PowerScenario.IsEnabled = true;
        DisplayScenarioButton.IsEnabled = true;
        DisplayRemovalScenarioButton.IsEnabled = true;
    }

    private string? GetDisplayPrerequisiteSkipReason()
    {
        if (displayScenario is null) return "Display acceptance scenario is unavailable.";
        var windowsOptIn = Environment.GetEnvironmentVariable("DESKTOP_PUPPY_WINDOWS_ACCEPTANCE");
        var displayOptIn = Environment.GetEnvironmentVariable("DESKTOP_PUPPY_DISPLAY_ACCEPTANCE");
        if (windowsOptIn != "1" || displayOptIn != "1")
            return displayScenario.CheckPrerequisites(windowsOptIn, displayOptIn, true, 0);

        var sessionReason = WindowsAcceptancePrerequisite.SessionSkipReason();
        if (sessionReason is not null) return sessionReason;
        var displayReason = displayScenario.CheckPrerequisites(
            windowsOptIn, displayOptIn, Environment.UserInteractive, remoteSessionMetric: 0);
        var prerequisiteReason = displayReason ?? WindowsAcceptancePrerequisite.SkipReason();
        if (prerequisiteReason is not null) return prerequisiteReason;
        if (scenario == "display-removal")
        {
            var monitors = displayScenario.GetMonitors();
            var target = monitors.FirstOrDefault(item => item.Alias == "Display 2");
            if (monitors.Count < 2 || target is null)
                return "At least two connected displays are required and run-local Display 2 must be available; the scenario was not started.";
            if (target.IsPrimary)
                return "The run-local Display 2 target must be secondary; choose a different connected display topology.";
        }
        return null;
    }

    private async void Begin(object sender, RoutedEventArgs e)
    {
        if (scenario is "display" or "display-removal")
        {
            await BeginDisplayAsync();
            return;
        }
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

    private async Task BeginDisplayAsync()
    {
        if (displayScenario is null) return;
        var displaySkipReason = GetDisplayPrerequisiteSkipReason();
        if (displaySkipReason is not null)
        {
            ShowResult(AcceptanceStatus.Skipped, displaySkipReason);
            DisposeDisplayScenario();
            return;
        }
        if (!CheckPrerequisites())
        {
            DisposeDisplayScenario();
            return;
        }
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
        DisplayScenarioButton.IsEnabled = false;
        DisplayRemovalScenarioButton.IsEnabled = false;
        CaptureDisplayButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ResultState.Text = scenario == "display-removal"
            ? "Starting isolated monitor-removal recovery run"
            : "Starting isolated mixed-DPI display run";
        ResultDetail.Text = "Preparing a private database and one isolated Desktop Puppy process after all opt-in, session, and monitor gates.";
        startCancellation = new CancellationTokenSource();
        DisplayAcceptanceRun? candidate = null;
        try
        {
            var scenarioForRun = displayScenario;
            pendingDisplayStartup = Task.Run(() => DisplayAcceptanceRun.StartAsync(
                Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE")!,
                scenarioForRun,
                startCancellation.Token,
                GetDisplayPrerequisiteSkipReason));
            candidate = await pendingDisplayStartup;
            if (closing || startCancellation.IsCancellationRequested)
            {
                await candidate.DisposeAsync();
                candidate = null;
                return;
            }

            displayRun = candidate;
            candidate = null;
            displayPetHwnd = displayRun.PetHwnd;
            displayMonitors = scenarioForRun.RefreshMonitors();
            if (scenario == "display-removal" &&
                (displayMonitors.Count < 2 || displayMonitors.FirstOrDefault(item => item.Alias == "Display 2") is not { IsPrimary: false }))
                throw new AcceptanceSkippedException("The run-local Display 2 secondary target changed before the isolated run was ready.");
            displayObservations.Clear();
            scenarioCancellation = new CancellationTokenSource();
            if (scenario == "display-removal")
            {
                ResultState.Text = "Waiting for operator to place pet on Display 2";
                ResultDetail.Text = "The same isolated pet HWND is ready. No display change or database action beyond this isolated run is initiated.";
                var target = displayMonitors.Single(item => item.Alias == "Display 2");
                Instructions.Text = $"A live pet HWND was discovered through UI Automation for the isolated app PID. Manually drag that real test pet onto {target.Alias} and let normal WPF placement settle. Click capture before disconnecting or disabling that display. After capture, the utility waits up to five minutes for the actual Windows display-change event and refreshed enumeration. Cancel at any time.";
                CaptureDisplayButton.Content = $"I moved the pet to {target.Alias} — capture pre-removal bounds";
            }
            else
            {
                ResultState.Text = "Waiting for operator-confirmed display move";
                ResultDetail.Text = "The same isolated pet HWND is ready. Capture the first display after your manual move.";
                Instructions.Text = $"A single live pet HWND was discovered through UI Automation for the isolated app PID. Manually drag that same pet near a work-area edge on {displayMonitors[0].Alias}, let placement settle, then click the button to confirm and capture its DPI/physical geometry. The second confirmation is for {displayMonitors[1].Alias}. The app remains isolated and no input or placement is injected by the utility.";
                CaptureDisplayButton.Content = $"I moved the same pet to {displayMonitors[0].Alias} — capture";
            }
            CaptureDisplayButton.IsEnabled = true;
        }
        catch (AcceptanceSkippedException ex)
        {
            if (displayRun is not null)
            {
                if (!await EndDisplayRunSafelyAsync()) return;
            }
            else
            {
                DisposeDisplayScenario();
            }
            ShowResult(AcceptanceStatus.Skipped, ex.Message);
        }
        catch (OperationCanceledException)
        {
            ShowResult(AcceptanceStatus.Incomplete, "Display run startup cancelled; no acceptance verdict was produced.");
            await EndDisplayRunSafelyAsync();
        }
        catch (Exception)
        {
            ShowResult(AcceptanceStatus.Failed,
                "The isolated display run or UI Automation pet-window discovery failed; no hardware identifiers or unrelated titles were reported.");
            await EndDisplayRunSafelyAsync();
            DisposeDisplayScenario();
        }
        finally
        {
            if (candidate is not null)
            {
                try { await candidate.DisposeAsync(); }
                catch (Exception) { ShowResult(AcceptanceStatus.Failed, "An owned display-run child could not be fully cleaned up."); }
            }
            startCancellation?.Dispose();
            startCancellation = null;
            pendingDisplayStartup = null;
        }
    }

    private async void CaptureDisplayObservation(object sender, RoutedEventArgs e)
    {
        if (displayRun is null || displayScenario is null || displayMonitors is null ||
            displayObservations.Count >= 2 || scenarioCancellation is null)
            return;

        CaptureDisplayButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        pendingScenario = CaptureDisplayObservationAsync();
        try { await pendingScenario; }
        finally { pendingScenario = null; }
    }

    private async Task CaptureDisplayObservationAsync()
    {
        if (scenario == "display-removal")
        {
            await CaptureRemovalRequestAndRunAsync();
            return;
        }

        var currentRun = displayRun;
        var currentScenario = displayScenario;
        var availableMonitors = displayMonitors;
        var cancellationToken = scenarioCancellation?.Token ?? CancellationToken.None;
        if (currentRun is null || currentScenario is null || availableMonitors is null) return;

        try
        {
            var target = availableMonitors[displayObservations.Count];
            ResultState.Text = $"Waiting for {target.Alias} placement to settle";
            ResultDetail.Text = "The utility is sampling only the same HWND's UIA identity and read-only Native geometry.";
            var observation = await currentScenario.ObserveWhenSettledAsync(
                target.Alias,
                displayPetHwnd,
                currentRun.ProductProcessId,
                () => displayRun?.IsProductRunning == true,
                cancellationToken);
            if (!string.Equals(observation.MonitorAlias, target.Alias, StringComparison.Ordinal))
            {
                await EndDisplayRunAsync();
                ShowResult(AcceptanceStatus.Failed,
                    $"The same pet HWND was confirmed for {target.Alias}, but native monitor lookup measured {observation.MonitorAlias}.");
                return;
            }

            displayObservations.Add(observation);
            if (displayObservations.Count == 1)
            {
                ResultState.Text = "Observation 1 of 2 recorded";
                ResultDetail.Text = FormatObservation(observation);
                Instructions.Text = $"First manual confirmation recorded for {target.Alias}. Now manually drag the same pet window near a work-area edge on {availableMonitors[1].Alias}, allow production placement reconciliation to settle, and confirm the second observation.";
                CaptureDisplayButton.Content = $"I moved the same pet to {availableMonitors[1].Alias} — capture";
                CaptureDisplayButton.IsEnabled = true;
                return;
            }

            var result = DisplayAcceptanceScenario.Validate(availableMonitors, displayObservations, currentRun.ProductProcessId);
            await EndDisplayRunAsync();
            ShowResult(result.Status, result.Summary);
        }
        catch (OperationCanceledException)
        {
            ShowResult(AcceptanceStatus.Incomplete, "Display observation cancelled; no acceptance verdict was produced.");
            await EndDisplayRunSafelyAsync();
        }
        catch (Exception)
        {
            ShowResult(AcceptanceStatus.Failed,
                "Read-only UIA/Native display observation failed; no monitor identifiers, HWND values, or unrelated titles were reported.");
            await EndDisplayRunSafelyAsync();
        }
    }

    private async Task CaptureRemovalRequestAndRunAsync()
    {
        var currentRun = displayRun;
        var currentScenario = displayScenario;
        var availableMonitors = displayMonitors;
        var cancellationToken = scenarioCancellation?.Token ?? CancellationToken.None;
        if (currentRun is null || currentScenario is null || availableMonitors is null || availableMonitors.Count < 2)
            return;

        try
        {
            var target = availableMonitors.Single(item => item.Alias == "Display 2");
            ResultState.Text = $"Capturing pre-removal request on {target.Alias}";
            ResultDetail.Text = "Reading the same isolated pet HWND, its actual physical rectangle, and the connected primary work area.";
            var request = await currentScenario.CaptureRemovalRequestAsync(
                target.Alias, displayPetHwnd, currentRun.ProductProcessId,
                () => displayRun?.IsProductRunning == true, cancellationToken);
            CaptureDisplayButton.IsEnabled = false;
            ResultState.Text = "Waiting for manual monitor removal";
            ResultDetail.Text = "Captured pre-removal physical bounds. Disconnect or disable only the named secondary display manually; the utility will not alter or restore topology.";
            Instructions.Text = $"Captured request for {request.TargetAlias}. Now manually disconnect or disable only {request.TargetAlias} through Windows display controls. Do not remove the primary display. The app remains isolated and this controller stays responsive while waiting for the real DisplaySettingsChanged event plus fresh enumeration showing {request.TargetAlias} absent. Timeout is five minutes; Cancel reports Incomplete. Restore the display manually after the result.";

            var result = await currentScenario.RunRemovalRecoveryAsync(
                request,
                () => displayRun?.IsProductRunning == true,
                cancellationToken);
            if (!await EndDisplayRunSafelyAsync()) return;
            ShowResult(result.Status, result.Summary);
            Instructions.Text += "\n\nTopology is never restored automatically. Restore the display manually using Windows controls.";
        }
        catch (DisplayRemovalPreconditionException ex)
        {
            if (await EndDisplayRunSafelyAsync())
                ShowResult(AcceptanceStatus.Skipped, ex.Message);
        }
        catch (TimeoutException)
        {
            if (await EndDisplayRunSafelyAsync())
                ShowResult(AcceptanceStatus.Incomplete, "Timed out while waiting for stable pre-removal placement; no topology-recovery verdict was produced.");
        }
        catch (OperationCanceledException)
        {
            if (await EndDisplayRunSafelyAsync())
                ShowResult(AcceptanceStatus.Incomplete, "Operator cancelled; no topology-recovery acceptance verdict was produced.");
        }
        catch (Exception)
        {
            if (!await EndDisplayRunSafelyAsync()) return;
            ShowResult(AcceptanceStatus.Failed,
                "The captured request or read-only native topology recovery observation failed; no hardware identifiers were reported.");
        }
    }

    private static string FormatObservation(DisplayAcceptanceObservation observation) =>
        $"{observation.TargetAlias}: {observation.Dpi} DPI; physical bounds " +
        $"[{observation.Bounds.Left},{observation.Bounds.Top},{observation.Bounds.Right},{observation.Bounds.Bottom}]; " +
        $"work area [{observation.WorkArea.Left},{observation.WorkArea.Top},{observation.WorkArea.Right},{observation.WorkArea.Bottom}].";

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
        CaptureDisplayButton.IsEnabled = false;
        ResultState.Text = AcceptanceStatus.Incomplete.ToString();
        ResultDetail.Text = "Operator cancelled; no acceptance verdict was produced.";
        if (pendingScenario is null && pendingDisplayStartup is null)
        {
            await EndRunSafelyAsync();
            await EndDisplayRunSafelyAsync();
        }
    }

    private void ShowResult(AcceptanceStatus status, string detail)
    {
        if (closing) return;
        ResultState.Text = status.ToString();
        ResultDetail.Text = detail;
        BeginButton.IsEnabled = status != AcceptanceStatus.Skipped &&
            (scenario is "lock" or "power" or "display" or "display-removal");
        CompleteButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        CaptureDisplayButton.IsEnabled = false;
        LockScenario.IsEnabled = status != AcceptanceStatus.Skipped;
        PowerScenario.IsEnabled = status != AcceptanceStatus.Skipped;
        DisplayScenarioButton.IsEnabled = true;
        DisplayRemovalScenarioButton.IsEnabled = true;
    }

    private async Task EndRunAsync()
    {
        var ending = run;
        run = null;
        if (ending is null) return;
        ending.EventsChanged -= OnEventsChanged;
        await ending.DisposeAsync();
    }

    private async Task EndDisplayRunAsync()
    {
        var ending = displayRun;
        displayRun = null;
        if (ending is not null)
        {
            try { await ending.DisposeAsync(); }
            finally { displayScenario = null; }
        }
        else DisposeDisplayScenario();
        displayMonitors = null;
        displayPetHwnd = IntPtr.Zero;
        displayObservations.Clear();
        scenarioCancellation?.Dispose();
        scenarioCancellation = null;
    }

    private async Task<bool> EndDisplayRunSafelyAsync()
    {
        try
        {
            await EndDisplayRunAsync();
            return true;
        }
        catch (Exception)
        {
            ShowResult(AcceptanceStatus.Failed,
                "The owned display app could not be fully stopped; its private run directory was retained for safety.");
            return false;
        }
    }

    private void DisposeDisplayScenario()
    {
        displayScenario?.Dispose();
        displayScenario = null;
        displayMonitors = null;
        displayPetHwnd = IntPtr.Zero;
        displayObservations.Clear();
        CaptureDisplayButton.IsEnabled = false;
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
            if (pendingDisplayStartup is not null)
            {
                try { await (await pendingDisplayStartup).DisposeAsync(); }
                catch (Exception) { /* Startup failure or cleanup is reported without sensitive details. */ }
            }
            if (pendingScenario is not null)
            {
                try { await pendingScenario; }
                catch (Exception) { /* Scenario cancellation is incomplete, not a passing verdict. */ }
            }
            await EndRunSafelyAsync();
            await EndDisplayRunSafelyAsync();
            DisposeDisplayScenario();
        }
        finally
        {
            base.OnClosed(e);
            Application.Current.Shutdown();
        }
    }
}
