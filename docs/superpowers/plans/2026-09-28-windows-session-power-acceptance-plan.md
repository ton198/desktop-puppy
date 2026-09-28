# Real Windows Session and Power Acceptance Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide a safe, operator-guided Windows acceptance tool that proves actual lock/unlock and suspend/resume notifications reach the running app and create the expected SQLite session boundaries.

**Architecture:** Add a separate, visible WPF acceptance utility rather than adding lock/sleep steps to the default xUnit suite. The utility starts Desktop Puppy against a unique temporary database, starts two independent foreground-fixture processes from its own executable, subscribes to real Windows notifications, guides an operator through each system transition, then checks persisted app sessions against observed event timestamps. All OS state changes remain manual.

**Tech Stack:** .NET 10 WPF, `SystemEventsSessionPowerEventSource`, `UsageSqliteStore`, Windows session/power events, SQLite, native process/window control only for isolated fixtures and observation. Fixture B is shown without activation so fixture A remains the initial foreground target.

**Spec:** `docs/superpowers/specs/2026-09-28-windows-e2e-acceptance-design.md` sections 3.2, 4, 5 E2E-POWER-01/E2E-POWER-02, 6, 7, 8, and 9.

## Global Constraints

- The acceptance utility is opt-in through `DESKTOP_PUPPY_WINDOWS_ACCEPTANCE=1`, requires a visible local interactive Windows session, is never part of the normal unit/UI test filters, and must show `Skipped` before creating run files if another `DesktopPuppy` process is already running.
- The operator alone initiates lock, unlock, sleep, wake, and any sign-in; the utility never calls `LockWorkStation`, `SetSuspendState`, unlock APIs, credential APIs, or router injection.
- Start the real app with `DESKTOP_PUPPY_USAGE_DB_PATH` set to a newly created temp DB. Only delete the utility's own verified temp directory after all child processes exit.
- Use the real `SystemEventsSessionPowerEventSource(new SystemClock())` in both app and observer. Mock providers or synthetic router events cannot satisfy acceptance.
- Distinguish `Passed`, `Failed`, `Incomplete/Cancelled`, and `Skipped/Prerequisite unmet`; incomplete or skipped is never a pass.
- The acceptance UI and logs must not record unrelated foreground titles, user activity, credentials, or complete hardware identifiers.
- Each acceptance scenario documents **Purpose**, **Coverage**, and **Target effect** in its visible instructions and source.
- Preserve existing user changes; do not stage or alter unrelated files.

---

## File Map

- `tests/DesktopPuppy.WindowsAcceptance.Core/DesktopPuppy.WindowsAcceptance.Core.csproj` — testable non-UI acceptance result, owned-temp-directory and session-boundary validation logic.
- `tests/DesktopPuppy.WindowsAcceptance.Core/AcceptanceResult.cs`, `AcceptanceOwnedDirectory.cs`, and `SessionBoundaryValidator.cs` — explicit outcomes, scoped temporary cleanup, and pure event/session-edge assertions.
- `tests/DesktopPuppy.WindowsAcceptance/DesktopPuppy.WindowsAcceptance.csproj` — standalone opt-in WPF executable referencing the app and acceptance-core assemblies; not a default test project.
- `tests/DesktopPuppy.WindowsAcceptance/App.xaml` and `App.xaml.cs` — start controller or fixture mode, validate opt-in/local-session prerequisites, and own global cleanup.
- `tests/DesktopPuppy.WindowsAcceptance/WindowsAcceptancePrerequisite.cs` — require `Environment.UserInteractive` and reject remote sessions through `GetSystemMetrics(SM_REMOTESESSION)` before launching any child.
- `tests/DesktopPuppy.WindowsAcceptance/WindowsAcceptanceWindow.xaml` and `.xaml.cs` — visible step-by-step lock/power instructions, operator confirmations, current result status, and final report.
- `tests/DesktopPuppy.WindowsAcceptance/AcceptanceRun.cs` — unique temp directory/database, real app process, fixture processes, event subscriptions, timeout state, SQLite assertions, process shutdown, and scoped cleanup.
- `tests/DesktopPuppy.WindowsAcceptance/ForegroundFixtureWindow.xaml` and `.xaml.cs` — stable test-only foreground target window for a child process launched with `--fixture <run-id> <fixture-id>`.
- `tests/DesktopPuppy.WindowsAcceptance/README.md` — prerequisites, opt-in launch instructions, user actions, expected status meanings, and troubleshooting.
- `tests/DesktopPuppy.Tests/WindowsAcceptance/AcceptanceCoreTests.cs` and `tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj` — deterministic tests for scoped temp storage and acceptance verdict rules.
- `DesktopPuppy.sln` — include the acceptance core and visible utility under the `tests` solution folder without making the utility an xUnit test.
- `docs/implementation-status.md` — record implementation and exact verification state; actual lock/suspend acceptance remains “not run” until an operator performs it.

## Task 1: Create the Opt-In Interactive Acceptance Hos

**Files:**
- Create: `tests/DesktopPuppy.WindowsAcceptance.Core/DesktopPuppy.WindowsAcceptance.Core.csproj
- Create: `tests/DesktopPuppy.WindowsAcceptance.Core/AcceptanceResult.cs
- Create: `tests/DesktopPuppy.WindowsAcceptance.Core/AcceptanceOwnedDirectory.cs
- Create: `tests/DesktopPuppy.WindowsAcceptance.Core/SessionBoundaryValidator.cs
- Create: `tests/DesktopPuppy.Tests/WindowsAcceptance/AcceptanceCoreTests.cs
- Modify: `tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj
- Create: `tests/DesktopPuppy.WindowsAcceptance/DesktopPuppy.WindowsAcceptance.csproj
- Create: `tests/DesktopPuppy.WindowsAcceptance/App.xaml
- Create: `tests/DesktopPuppy.WindowsAcceptance/App.xaml.cs
- Create: `tests/DesktopPuppy.WindowsAcceptance/WindowsAcceptancePrerequisite.cs
- Create: `tests/DesktopPuppy.WindowsAcceptance/WindowsAcceptanceWindow.xaml
- Create: `tests/DesktopPuppy.WindowsAcceptance/WindowsAcceptanceWindow.xaml.cs
- Modify: `DesktopPuppy.sln

**Interfaces:**

```csharp
public enum AcceptanceStatus { Passed, Failed, Incomplete, Skipped }
public enum ObservedSystemEventKind { Locked, Unlocked, Suspend, Resume }
public sealed record AcceptanceResult(AcceptanceStatus Status, string Summary, IReadOnlyList<ObservedSystemEvent> ObservedEvents, IReadOnlyList<string> SafeDiagnostics);
public sealed record ObservedSystemEvent(ObservedSystemEventKind Kind, DateTimeOffset At);
public sealed record ObservedUsageSession(uint ProcessId, DateTimeOffset StartedAt, DateTimeOffset EndedAt);
public sealed record FixtureProcessIds(uint FixtureAProcessId, uint FixtureBProcessId);

public sealed class AcceptanceOwnedDirectory
{
    public static AcceptanceOwnedDirectory Create();
    public string DatabasePath { get; }
    public void DeleteOwned();
}

public sealed class SessionBoundaryValidator
{
    public AcceptanceResult ValidateLockUnlock(
        IReadOnlyList<ObservedSystemEvent> events,
        IReadOnlyList<ObservedUsageSession> sessions,
        FixtureProcessIds fixtureProcessIds,
        DateTimeOffset operatorCompletedAt,
        bool operatorConfirmed);
    public AcceptanceResult ValidateSuspendResume(
        IReadOnlyList<ObservedSystemEvent> events,
        IReadOnlyList<ObservedUsageSession> sessions,
        FixtureProcessIds fixtureProcessIds,
        DateTimeOffset operatorCompletedAt,
        bool operatorConfirmed);
}


Controller consumes `DESKTOP_PUPPY_WINDOWS_ACCEPTANCE`, `DESKTOP_PUPPY_EXE`, and the current Windows interactive-session state. It produces two visible scenario entry points (`Run lock/unlock acceptance`, `Run suspend/resume acceptance`) and a result state: `Not started / Waiting for operator / Passed / Failed / Incomplete / Skipped`. Fixture mode is selected by args `--fixture <run-id> <fixture-id>`; other launches open the controller. Fixture B uses `ShowActivated="False"` so creating it does not steal initial foreground from fixture A.

- [ ] **Step 1: Create the WPF project and add it to the solution.** Target `net10.0-windows`, set `UseWPF=true`, enable nullable/implicit usings/Windows targeting consistently with current test projects, and reference both `src/DesktopPuppy/DesktopPuppy.csproj` and `tests/DesktopPuppy.WindowsAcceptance.Core/DesktopPuppy.WindowsAcceptance.Core.csproj`. Keep it out of default xUnit invocation.
- [ ] **Step 2: Add prerequisite checks before creating run data or launching children.** If the opt-in variable is absent/not `1`, show `Skipped` and launch nothing. Validate `DESKTOP_PUPPY_EXE`. Require `Environment.UserInteractive` and `GetSystemMetrics(SM_REMOTESESSION) == 0`; otherwise show `Skipped` and create no DB/process. Before creating the owned directory, check for an already-running `DesktopPuppy` process; show `Skipped` with a safe reason and leave it untouched, because the product single-instance guard would activate the unrelated process instead of launching against this run isolated DB.
- [ ] **Step 3: Add visible instructions with Purpose/Coverage/Target effect.** Lock instructions require manual lock/unlock, focus of fixture A, switch to fixture B, then return to controller to confirm. Power instructions require manual sleep/wake, optional manual unlock, focus of fixture A, switch to fixture B, then return to controller. The controller explains that fixture A is the required first target and fixture B is used to close the resumed session; the operator must not begin the system transition until fixture A is foreground. Provide `Begin`, `I have completed the step`, and `Cancel`; no UI button changes lock or power state.
- [ ] **Step 4: Reference the core project from `DesktopPuppy.Tests.csproj` and write the seven core unit tests listed below.** Add separate Purpose/Coverage/Target effect comments above each test. Run the focused `AcceptanceCoreTests` filter and confirm RED errors point to missing acceptance-core types/rules.
**Acceptance core unit cases; place Purpose/Coverage/Target effect comments above each `[Fact]`:**
1. `Owned_temp_directory_is_unique_and_scoped` — **Purpose:** avoid cross-run DB collisions; **Coverage:** two generated owners and normalized paths; **Target effect:** distinct DB files remain under the fixed temp root.
2. `Owned_temp_directory_cleanup_preserves_neighbor_files` — **Purpose:** prevent cleanup from deleting user files; **Coverage:** generated run directory, sibling directory and outside sentinel; **Target effect:** cleanup removes only the run directory and preserves sibling/outside files.
3. `Lock_verdict_requires_real_ordered_events_and_closed_edges` — **Purpose:** prevent simulated/incomplete system flows from passing; **Coverage:** locked/unlocked event order, pre/post fixture sessions and completion time; **Target effect:** only correct closed edges return `Passed`.
4. `Suspend_verdict_requires_ordered_events_and_closed_edges` — **Purpose:** prove a legitimate suspend/resume flow can pass only from real ordered event observations and closed product sessions; **Coverage:** Suspend then Resume, a pre-suspend fixture session, a post-resume fixture session and operator completion; **Target effect:** a correct split returns `Passed`.
5. `Suspend_verdict_rejects_usage_overlap` — **Purpose:** ensure suspended time is excluded; **Coverage:** Suspend/Resume timestamps and a fixture interval overlapping that range; **Target effect:** overlapping sessions return `Failed`.
6. `Missing_operator_completion_is_incomplete` — **Purpose:** do not report acceptance when the user skipped the manual step; **Coverage:** absent completion confirmation despite partial events; **Target effect:** result is `Incomplete`, never `Passed`.
7. `Missing_or_reordered_system_event_is_failed` — **Purpose:** require an actual ordered Windows event chain for both scenarios; **Coverage:** missing event or Unlocked-before-Locked / Resume-before-Suspend sequences; **Target effect:** outcome is `Failed`, never `Passed`.

- [ ] **Step 5: Implement the acceptance core.** Create the plain `net10.0` core library, reference it from `DesktopPuppy.Tests.csproj` and the WPF utility, implement unique scoped directory ownership and the pure session-boundary verdict rules, then rerun the focused `AcceptanceCoreTests` filter. Expected: all seven behaviors above pass without starting the acceptance UI or touching Windows system state.
- [ ] **Step 6: Build the project and full solution.** Run:

```powershell
dotnet build tests/DesktopPuppy.WindowsAcceptance.Core/DesktopPuppy.WindowsAcceptance.Core.csproj --configuration Release
dotnet build tests/DesktopPuppy.WindowsAcceptance/DesktopPuppy.WindowsAcceptance.csproj --configuration Release
dotnet build DesktopPuppy.sln --configuration Release


Expected: all builds pass and normal solution test discovery does not execute an operator workflow.

## Task 2: Launch Isolated App, Fixture Processes, and Observe Real Events

**Files:**
- Create: `tests/DesktopPuppy.WindowsAcceptance/AcceptanceRun.cs
- Create: `tests/DesktopPuppy.WindowsAcceptance/ForegroundFixtureWindow.xaml
- Create: `tests/DesktopPuppy.WindowsAcceptance/ForegroundFixtureWindow.xaml.cs
- Modify: `tests/DesktopPuppy.WindowsAcceptance/App.xaml.cs
- Modify: `tests/DesktopPuppy.WindowsAcceptance/WindowsAcceptanceWindow.xaml.cs

**Interfaces:**

```csharp
public static Task<AcceptanceRun> StartAsync(string appExecutablePath, CancellationToken cancellationToken);
public Task<AcceptanceResult> RunLockUnlockAsync(CancellationToken cancellationToken);
public Task<AcceptanceResult> RunSuspendResumeAsync(CancellationToken cancellationToken);
public ValueTask DisposeAsync();


`StartAsync` creates a GUID-owned directory under `%TEMP%\DesktopPuppy.WindowsAcceptance`, initializes `usage.db`, launches the app with `DESKTOP_PUPPY_USAGE_DB_PATH`, and launches two fixture child processes with distinct PIDs. The controller subscribes using `new SystemEventsSessionPowerEventSource(new SystemClock())`; it records only event enum and timestamp for Locked/Unlocked/Suspend/Resume. `DisposeAsync` closes children, releases event/store handles, waits for exit, then deletes only that run directory.

- [ ] **Step 1: Use the already unit-tested scoped directory owner.** In the acceptance utility, verify the run directory exists before DB creation. During `DisposeAsync`, stop child processes and close DB handles before `DeleteOwned()`; never accept a recursive-delete target from user input.
- [ ] **Step 2: Initialize isolated DB and launch the real product.** Use `UsageSqliteStore(databasePath).Initialize()`, then `ProcessStartInfo` with `UseShellExecute=false` and only the isolated DB environment override. Verify the process starts and the DB remains under the owned directory.
- [ ] **Step 3: Implement fixture mode and launch two independent fixture processes.** A fixture child displays a stable generic title `Desktop Puppy Acceptance Target` and a run-local Automation ID. The controller stores only child PIDs and fixture IDs; it passes both named PIDs to `SessionBoundaryValidator` so fixture-A and fixture-B session edges cannot be confused. No run ID or actual window title is written to result logs.
- [ ] **Step 4: Subscribe to real events and marshal updates.** Start production `SystemEventsSessionPowerEventSource` with `SystemClock`; dispatch observer state to WPF UI thread; dispose subscriptions idempotently on completion, error, cancellation, and exit.
- [ ] **Step 5: Test default opt-out.** Run the utility with no opt-in variable; expected: visible `Skipped`, no Desktop Puppy or fixture process, and no temp database directory. Build Release after the change.

## Task 3: Implement Lock/Unlock and Persistent Boundary Assertions

**E2E-POWER-01 — Real lock/unlock session split**
- **Purpose:** Verify actual Windows lock/unlock notifications reach the app and stop counting locked-screen time.
- **Coverage:** Foreground fixture A, actual lock/unlock, observer event timestamps, product SQLite session edges, focus transitions A-to-B-to-controller, and separate PIDs for both fixtures.
- **Target effect:** Pre-lock fixture-A usage ends near Locked, no fixture row spans the locked interval beyond tolerance, and fixture A resumes after Unlocked; switching to B closes A and returning to the controller closes B.


**Files:**
- Modify: `tests/DesktopPuppy.WindowsAcceptance/AcceptanceRun.cs
- Modify: `tests/DesktopPuppy.WindowsAcceptance/WindowsAcceptanceWindow.xaml
- Modify: `tests/DesktopPuppy.WindowsAcceptance/WindowsAcceptanceWindow.xaml.cs
- Create: `tests/DesktopPuppy.WindowsAcceptance/README.md

**Interfaces:** `RunLockUnlockAsync` waits for the operator-confirmed flow and actual `Locked` then `Unlocked` notifications, then evaluates only closed `UsageSqliteStore.ReadSessions()` rows for fixture PIDs. A scenario returns `AcceptanceResult(Status, Summary, ObservedEvents, SafeDiagnostics)`; diagnostics exclude titles and raw DB rows.

- [ ] **Step 1: Add lock scenario statements and operator state machine.** Keep instructions visible before lock. The UI thread remains unblocked while Windows is locked. On return, instruct the operator to focus fixture A, then switch to fixture B to close the post-unlock session for A and start a second fixture session; returning to the controller to confirm completion closes fixture B. Validate the closed fixture-A session as evidence that sampling resumed after unlock, and validate fixture B closes at the confirmation transition. Do not require a session end timestamp later than the confirmation timestamp: activating the controller causes the foreground change before its button handler records completion.
- [ ] **Step 2: Add filtered persisted-session queries.** Open a second `UsageSqliteStore` instance and select rows by fixture PID plus acceptance process name. Do not log row data or unrelated processes.
- [ ] **Step 3: Assert real event order and session edges.** Require one Locked and one later Unlocked timestamp; the fixture-A pre-lock session must end within 5 seconds of Locked; no fixture row may overlap the locked interval beyond that tolerance; a post-unlock fixture-A row must start no earlier than 5 seconds before Unlocked and must be closed by switching to fixture B; fixture B must start after that switch and close within 5 seconds before the controller records completion. Wrong/missing events or boundaries are `Failed`; no user action/cancel is `Incomplete`; unmet prerequisites are `Skipped`.
- [ ] **Step 4: Add bounded waiting and cancellation.** Show a configurable 30-minute deadline for the lock/unlock and suspend/resume operator flow, controlled by `DESKTOP_PUPPY_ACCEPTANCE_STEP_TIMEOUT_MINUTES` and validated to 1-120 minutes before starting. Timeout reports `Incomplete` and names the pending step. Cancel shuts down fixture/app children, releases notifications, and removes only the owned temp directory.
- [ ] **Step 5: Document and build the lock flow.** README lists Purpose/Coverage/Target effect, local interactive prerequisite, exact manual steps, opt-in variables, statuses, timeout, and cleanup. Build and launch only with the visible controller.

## Task 4: Implement Suspend/Resume Scenario

**E2E-POWER-02 — Real suspend/resume session split**
- **Purpose:** Verify actual Windows Suspend/Resume notifications reach the app and collection restarts after the machine resumes.
- **Coverage:** Pre-suspend fixture-A session, manual system sleep/wake, actual notifications, optional manual unlock, post-resume A-to-B-to-controller transitions and SQLite boundaries for both PIDs.
- **Target effect:** The first session closes near Suspend, no fixture usage overlaps the suspended interval, and a closed fixture-A session is observed after Resume when the operator switches to fixture B; fixture B closes when the operator returns to the controller to complete the step. The result does not classify sleep versus hibernate.


**Files:**
- Modify: `tests/DesktopPuppy.WindowsAcceptance/AcceptanceRun.cs
- Modify: `tests/DesktopPuppy.WindowsAcceptance/WindowsAcceptanceWindow.xaml
- Modify: `tests/DesktopPuppy.WindowsAcceptance/WindowsAcceptanceWindow.xaml.cs
- Modify: `tests/DesktopPuppy.WindowsAcceptance/README.md

- [ ] **Step 1: Add the visible manual suspend/resume workflow.** Tell the operator to save unrelated work, manually sleep the machine, wake it, unlock if requested, focus fixture A, switch to fixture B to close A resumed session, then return to the controller and confirm. Do not call power-control APIs or change power settings.
- [ ] **Step 2: Verify real Suspend/Resume order and persisted session edges.** Require actual Suspend then Resume events. The pre-suspend fixture row must end within 15 seconds of observed Suspend; no fixture row overlaps the suspend interval beyond 15 seconds; a post-resume fixture-A row must start no earlier than 15 seconds before Resume and close when the operator switches to fixture B; fixture B must close within 15 seconds before the controller records completion. The result says generic Suspend/Resume and never distinguishes hibernate from sleep.
- [ ] **Step 3: Verify incomplete flow cannot pass.** Manually run one cancelled flow and confirm `Incomplete`. Run one completed flow only with operator consent; `Passed` requires both real notifications and correct persisted boundaries.
- [ ] **Step 4: Run normal regressions.** Run the existing unit and `Category=Ui` suites. Confirm neither launches the acceptance controller or performs a system operation.

## Task 5: Record Evidence Without Overstating Hardware Results

**Files:**
- Modify: `docs/implementation-status.md
- Modify: `tests/DesktopPuppy.WindowsAcceptance/README.md

- [ ] **Step 1: Record delivered automation and exact build/test output.** State the guided harness exists. Keep actual lock/unlock and suspend/resume marked `Not run` until an operator completes each scenario on Windows.
- [ ] **Step 2: Review cleanup and safety.** Confirm there are no calls to lock/sleep/wake/unlock, credential APIs, synthetic event injection, or cleanup outside the owned temp directory. Confirm no production DB is opened.

## Completion Checklis

- [ ] Lock and suspend scenarios require visible manual action and explicit opt-in.
- [ ] Actual `SystemEvents` notifications and persisted product DB boundaries are both required for `Passed`.
- [ ] App/fixture processes use an isolated temporary DB and are cleaned up on pass, fail, cancel, and timeout.
- [ ] Not-run, skipped, incomplete, failed, and passed remain distinct in the UI and status file.
