# Windows session acceptance — E2E-POWER-01

## E2E-POWER-01 — Real lock/unlock session split

**Purpose:** Verify actual Windows lock/unlock notifications reach the app and stop counting locked-screen time.

**Coverage:** Foreground fixture A, operator-initiated lock/unlock, observer event timestamps, product SQLite session edges, focus transitions A-to-B-to-controller, and separate PIDs for both fixtures.

**Target effect:** Pre-lock fixture-A usage ends within 5 seconds of `Locked`; no fixture row spans the locked interval by more than 5 seconds; fixture A resumes after `Unlocked`; switching to B closes A; returning to the controller closes B within 5 seconds before confirmation is recorded.

## Prerequisites and opt-in

- Windows, in a local interactive session (not a remote session).
- `DESKTOP_PUPPY_WINDOWS_ACCEPTANCE=1` explicitly opts in.
- `DESKTOP_PUPPY_EXE` must be the absolute path to the product's `DesktopPuppy.exe`.
- No `DesktopPuppy` process may already be running. If one exists, the controller shows `Skipped` before creating any temporary data and does not activate, close, or otherwise interact with it.
- The operator must be able to lock and unlock/sign in to Windows manually.

The controller is opt-in and is not launched by builds or tests. Set the variables in the current PowerShell session before opening the visible acceptance controller, for example:

```powershell
$env:DESKTOP_PUPPY_WINDOWS_ACCEPTANCE = '1'
$env:DESKTOP_PUPPY_EXE = 'C:\absolute\path\to\DesktopPuppy.exe'
# Optional; defaults to 30 minutes. Valid values are whole minutes from 1 through 120.
$env:DESKTOP_PUPPY_ACCEPTANCE_STEP_TIMEOUT_MINUTES = '30'
```

Build the visible controller without launching it:

```powershell
dotnet build tests/DesktopPuppy.WindowsAcceptance/DesktopPuppy.WindowsAcceptance.csproj
```

Open the resulting `DesktopPuppy.WindowsAcceptance.exe` interactively only when ready to run the manual acceptance. Do not use `dotnet test` to trigger the workflow; tests never initiate or simulate an OS lock/unlock transition.

## Manual lock/unlock steps

1. In the visible controller, choose **Run lock/unlock acceptance** and confirm the stated Purpose, Coverage, Target effect, and displayed deadline.
2. Choose **Begin isolated lock scenario**. The controller checks prerequisites again before creating its owned temporary database.
3. Focus fixture A. The operator alone locks Windows.
4. The operator alone unlocks Windows and completes sign-in if Windows requires it. The utility never calls lock, unlock, credential, sleep, wake, or input-injection APIs.
5. Wait for the controller to observe the real ordered `Locked` and `Unlocked` notifications. After it reports the pair, focus fixture A, switch to fixture B, then return to the controller.
6. Choose **Confirm fixture switches complete**. Returning to the controller changes foreground first; the recorded completion time therefore follows the B session close.

The isolated app uses a private owned database and two distinct fixture processes. The controller reads only closed usage-session records for those fixture PIDs and the acceptance process name. It does not display or log window titles, raw rows, unrelated processes, user activity, credentials, or full hardware identifiers.

## Statuses and cleanup

- **Passed:** the real ordered Windows events and the required closed database session edges satisfy the 5-second boundaries.
- **Failed:** observed lock/unlock notifications are wrong/repeated/out of order, or persisted session boundaries do not match.
- **Incomplete:** the operator has not completed the required manual transitions before the deadline, or cancels; this can never pass.
- **Skipped:** opt-in/session/product prerequisites are unmet; this can never pass.

The optional `DESKTOP_PUPPY_ACCEPTANCE_STEP_TIMEOUT_MINUTES` setting defaults to 30 minutes and accepts 1–120 whole minutes. The lock flow measures its deadline normally. The suspend/resume flow measures awake time: its remaining deadline is paused on the observed Suspend notification and resumes on the observed Resume notification, so the time asleep does not cause a timeout.

On cancel or completion, the run closes its event observer and owned child-process handles before stopping its fixture/product children and deleting only its own temporary directory. If an owned child cannot be closed, cleanup retains the directory for safety. The unrelated DesktopPuppy instance, user data, and neighboring temp files are never cleaned up.

## E2E-POWER-02 — Real suspend/resume session split

**Purpose:** Verify actual Windows Suspend/Resume notifications reach the app and collection restarts after the machine resumes.

**Coverage:** Pre-suspend fixture-A session, operator-initiated system suspend/wake, optional manual unlock, post-resume A-to-B-to-controller transitions, and isolated SQLite boundaries for both fixture PIDs.

**Target effect:** Pre-suspend fixture A closes within 15 seconds of Suspend; no fixture usage overlaps the suspended interval by more than 15 seconds; a post-resume fixture-A session begins no earlier than 15 seconds before Resume and closes on the A-to-B switch; fixture B closes within 15 seconds before controller confirmation. Results use generic Suspend/Resume wording and do not distinguish sleep from hibernate.

### Manual suspend/resume steps

1. Save unrelated work. Choose **Run suspend/resume acceptance**, review Purpose, Coverage, Target effect, and the awake-time deadline, then choose **Begin isolated suspend/resume scenario**.
2. Focus fixture A. The operator alone manually puts Windows to sleep; the utility does not initiate a system transition or change power settings.
3. Wake Windows yourself. Unlock/sign in if requested. The deadline pauses after the actual Suspend notification and resumes after the actual Resume notification.
4. After the controller observes ordered Suspend then Resume, focus fixture A, switch to fixture B to close A's resumed session, then return to the controller and confirm.
5. Cancel or allow the awake-time deadline to expire to produce **Incomplete**, never **Passed**. A pass requires both real notifications, closed persisted boundaries, and operator confirmation.

Builds and existing unit/UI tests do not launch this manual flow or perform a system transition. Real Windows suspend/resume verification remains **Not run** until an operator completes the steps above.


## Current verification status — 2026-09-28

This harness is opt-in and operator-driven. Its controller build and automated tests do not start the manual scenarios. Actual Windows lock/unlock and suspend/resume acceptance are both **Not run** and remain so until an operator completes the corresponding scenario on Windows. Injected-provider/unit tests do not establish that Windows delivered real notifications.

Statuses are intentionally distinct:

- **Not run:** no operator execution has been recorded for that scenario.
- **Skipped:** opt-in or a preflight prerequisite prevented starting the scenario; this is not a pass.
- **Incomplete:** a started scenario was cancelled or reached its deadline before all required evidence/confirmation; this is not a pass.
- **Failed:** real observed events or persisted session boundaries contradict the scenario rules.
- **Passed:** only after the required real ordered OS notifications, isolated persisted session edges, and operator confirmation satisfy the scenario.

### Automated evidence (separate runs and scopes)

- **2026-09-28 — full unit suite after the Task 4 suspend regression:** `dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore` — **121 passed, 0 failed, 0 skipped**.
- **2026-09-28 — fresh serialized Release solution/controller build:** `dotnet build DesktopPuppy.sln --configuration Release --no-restore --no-incremental --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false` — **Build succeeded, 0 warnings, 0 errors**. The immediately following `dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-build --no-restore` — **121 passed, 0 failed, 0 skipped**. Earlier ordinary parallel/node-reuse attempts encountered WPF markup-cache access denied; the cause is not asserted, and the serialized build is the verified workaround.
- **2026-09-28 — safe UI smoke, historical Task 4 run:** `DesktopPuppyWindowUiTests` — **1/1 passed**. This is not the full UI category.
- **2026-09-26 — historical full `Category=Ui` run:** **11/11 passed** before the acceptance changes. The full category has **Not run** after these changes. It is intentionally not rerun here because its interaction tests use `SendInput` on the active desktop.
- **2026-09-28 — separate real-timer E2E:** `Category=LongRunningE2E` — **1/1 passed in 33 seconds**, confirming the actual 30-second statistics refresh. This does not verify lock/power delivery.

The 30-second timer refresh has real long-running automated evidence. Local-midnight rollover is separate: actual midnight behavior remains **Not run** manually. Do not infer it from injected-clock tests.

Do not combine the historical 11/11 UI result, distinct 1/1 safe smoke, long-running timer test, or current unit-suite result into a claim that the full UI suite passed after the acceptance branch changes.

## E2E-DISPLAY-01 — Opt-in mixed-DPI placement

**Purpose:** Verify the real WPF pet placement path against actual monitor geometry and per-window DPI.

**Coverage:** The operator manually moves the same Release app pet HWND to two connected displays and confirms each capture. UI Automation locates the `PetWindow` AutomationId under the isolated app PID; the utility revalidates that PID and AutomationId for the same HWND at each observation. Native monitor enumeration, HWND bounds, containing monitor, work area, and `GetDpiForWindow` are read through the product Native provider/positioner.

**Target effect:** With distinct measured DPI values, each capture must be on the requested run-local display alias, have the DPI-scaled physical WPF size, match `PetPlacementPolicy` edge clamping, and retain at least 20 visible physical pixels. Equal nonzero DPI measurements on the same HWND always produce **Skipped** and include the measured values.

### Prerequisites and opt-in

- Windows in a local interactive desktop session (not Remote Desktop).
- At least two enumerated connected displays.
- Set both `DESKTOP_PUPPY_WINDOWS_ACCEPTANCE=1` and `DESKTOP_PUPPY_DISPLAY_ACCEPTANCE=1` in the current PowerShell session.
- Set `DESKTOP_PUPPY_EXE` to the absolute path of the Release `DesktopPuppy.exe` to observe.
- No unrelated `DesktopPuppy` instance may already be running. The controller leaves an existing instance untouched and skips before creating the private run database or starting a child app.

Example configuration and build (building does not run the acceptance utility):

```powershell
$env:DESKTOP_PUPPY_WINDOWS_ACCEPTANCE = '1'
$env:DESKTOP_PUPPY_DISPLAY_ACCEPTANCE = '1'
$env:DESKTOP_PUPPY_EXE = 'C:\path\to\desktop-puppy\src\DesktopPuppy\bin\Release\net10.0-windows\DesktopPuppy.exe'
dotnet build src/DesktopPuppy/DesktopPuppy.csproj --configuration Release
dotnet build tests/DesktopPuppy.WindowsAcceptance/DesktopPuppy.WindowsAcceptance.csproj --configuration Release
```

Open `tests/DesktopPuppy.WindowsAcceptance/bin/Release/net10.0-windows/DesktopPuppy.WindowsAcceptance.exe` interactively only when ready for the manual scenario. The standalone utility is not started by xUnit, the default UI filter, or either build command. Missing opt-in, a nonlocal/noninteractive session, fewer than two displays, invalid executable prerequisites, or an existing product process results in **Skipped** before the isolated app/run database is created. Selecting the display scenario may create a read-only provider to enumerate displays; every skip path disposes it.

### Manual steps

1. Choose **Run mixed-DPI display acceptance** and review the displayed Purpose, Coverage, and Target effect.
2. Choose **Begin isolated mixed-DPI display scenario**. Prerequisites are checked again before creating a private temporary database or starting the isolated Release app.
3. After the isolated pet appears, manually drag that same pet window near an edge on **Display 1**. Let the product's normal DPI/location reconciliation settle, then press the matching confirmation/capture button.
4. Manually drag the same pet window to **Display 2**, allow reconciliation to settle, and confirm the second capture. The utility rejects a changed HWND, process PID, AutomationId, or containing-monitor mismatch.
5. Review the result. **Passed** requires distinct measured DPI, matching physical geometry, policy-compliant position, and 20-pixel visibility on both displays. Equal DPI reports **Skipped** with the measured display aliases and DPI values. Cancel or timeout never passes.

The acceptance utility does not inject input, call `SetWindowPos`, change display settings, or infer placement from screenshot pixels. It reports run-local display aliases, DPI, bounds, work areas, and status only; raw device IDs, HWND values, unrelated window titles, and user activity are not output. The app and database are private to this run. Cleanup deletes the owned directory only after the app process is affirmatively confirmed exited. If stopping the child fails or exit cannot be confirmed, the run directory is retained and the scenario/provider is still disposed; do not remove that retained directory until the child is confirmed stopped.

Actual mixed-DPI hardware verification remains **Not run** until an operator performs these steps on a qualifying setup. Deterministic fake-backed tests do not count as hardware acceptance.


## E2E-DISPLAY-02 — Monitor removal recovery

**Purpose:** Prove the app re-evaluates a pet window whose display disappears and applies the documented primary fallback.

**Coverage:** Pet on secondary display; operator manually disconnects/disables it; actual display-change event; refreshed monitor enumeration; captured pre-change request compared with primary-work-area policy result.

**Target effect:** Actual HWND bounds match the expected fallback within 1 physical pixel, remain at least 20 physical pixels visible, and the app remains running. Visibility alone does not pass.

### Prerequisites and opt-in

- Windows in a local interactive desktop session (not Remote Desktop).
- At least two enumerated connected displays. Run-local aliases put the primary first, so **Display 2** is a secondary target.
- Set both `DESKTOP_PUPPY_WINDOWS_ACCEPTANCE=1` and `DESKTOP_PUPPY_DISPLAY_ACCEPTANCE=1`.
- `DESKTOP_PUPPY_EXE` must be an absolute path to the Release `DesktopPuppy.exe`.
- If another `DesktopPuppy` process is running, the scenario reports **Skipped** before creating its isolated run database or starting the child app. The existing process is left untouched.

Selecting the scenario checks opt-in, local-session, executable/process, and monitor prerequisites. The checks are repeated before the isolated database is created. The operator must restore any manually removed display with Windows controls; the utility never changes or restores topology.

### Manual steps and acceptance rule

1. Choose **Run monitor-removal recovery acceptance** and review the displayed Purpose, Coverage, and Target effect. Choose **Begin isolated monitor-removal scenario**.
2. Manually drag the isolated real pet window to **Display 2**. After its placement settles, press the capture button. The utility records the isolated HWND/PID, actual physical pre-removal rectangle, run-local target/primary aliases, and current primary work area. It does not inspect internal placement-service cache state.
3. Manually disconnect or disable only **Display 2** through Windows controls. Keep the primary display connected. The controller remains responsive and waits for the actual `DisplaySettingsChanged` notification plus refreshed Native monitor enumeration showing the captured target absent, for up to **five minutes**. Cancel or timeout reports **Incomplete**; unmet prerequisites report **Skipped**.
4. After WPF processes the topology/DPI change and the same HWND geometry settles, the utility reads its live `GetDpiForWindow` value. It converts the pet's fixed animation-union WPF-DIP size (currently approximately 89×125) to physical extents, applies `PetPlacementPolicy.Clamp` to the **captured pre-removal physical left/top** and the **captured primary work area**, and independently compares that expected physical rectangle to the actual HWND bounds. The post-change bounds never redefine the request.
5. **Passed** requires the same HWND and live isolated process, the target absent, the captured primary still connected and primary, actual HWND membership on that primary, every physical edge within **1 pixel**, and at least **20 physical pixels** visible. Merely remaining visible cannot pass. Results contain run-local aliases, DPI, and geometry only—never full device identifiers.
6. Review the result and restore the display manually. Cleanup stops the isolated app and removes only its own run database directory after process exit is confirmed. If exit cannot be confirmed, the directory is retained for safety.

The acceptance executable is not launched by builds or tests. Deterministic tests use a fake Native provider and do not alter display settings or topology. Real E2E-DISPLAY-02 hardware acceptance remains **Not run** until an operator completes the manual flow on a qualifying Windows desktop.


### Task 5 verification record

- Cleanup regression **RED** before the fix: `dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --no-restore --filter FullyQualifiedName~Failed_display_child_cleanup_retains_run_directory_and_disposes_provider --verbosity minimal` failed at `Assert.False(deleted)` because the unsafe cleanup path deleted the directory after stop failed.
- Cleanup regression **GREEN** after the fix: `dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore -m:1 /nodeReuse:false --filter FullyQualifiedName~Failed_display_child_cleanup_retains_run_directory_and_disposes_provider --verbosity minimal` — 1 passed, 0 failed, 0 skipped. The test covers stop exception, unconfirmed exit, confirmed exit, and scenario/provider disposal.
- Release builds: `dotnet build src/DesktopPuppy/DesktopPuppy.csproj --configuration Release --no-restore -m:1 /nodeReuse:false` and `dotnet build tests/DesktopPuppy.WindowsAcceptance/DesktopPuppy.WindowsAcceptance.csproj --configuration Release --no-restore -m:1 /nodeReuse:false` — both succeeded with 0 warnings and 0 errors.
- Full deterministic suite: `dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore -m:1 /nodeReuse:false --verbosity minimal` — 162 passed, 0 failed, 0 skipped.
- `Category=Ui` was **Not run** because its interaction tests inject `SendInput` into the live desktop. The acceptance executable was not launched and no display hardware or topology was manipulated; E2E-DISPLAY-01 hardware acceptance is **Not run**. These automated checks do not establish real monitor/DPI behavior.

### Task 6 verification record

- Event-refresh regression **RED** before the retry fix: one post-capture `DisplaySettingsChanged` event followed by a transient native enumeration exception returned **Incomplete** at the bounded deadline instead of verifying the valid primary fallback; no second event was raised.
- Event-refresh regression **GREEN** after the fix: `dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~Monitor_removal_retries_transient_enumeration_failure_after_same_event` — **1 passed, 0 failed, 0 skipped**. The event-observed state remains set while fresh enumeration is retried.
- Fresh serialized Release solution build: `dotnet build DesktopPuppy.sln --configuration Release --no-restore --no-incremental --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false` — **0 warnings, 0 errors**.
- Fresh full deterministic test project: `dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-build --no-restore --verbosity minimal` — **174 passed, 0 failed, 0 skipped**.
- `git diff --check` — clean. `Category=Ui` was **Not run** to avoid active-desktop input injection. The acceptance utility was not launched; manual E2E-DISPLAY-02 hardware acceptance remains **Not run**. No display topology or settings were changed.