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

The optional `DESKTOP_PUPPY_ACCEPTANCE_STEP_TIMEOUT_MINUTES` setting defaults to 30 minutes and accepts 1–120 whole minutes. The same setting is intended for the later suspend/resume manual flow, but that scenario is deliberately not runnable in this Task 3 build; do not use it as an acceptance result yet.

On cancel or completion, the run closes its event observer and owned child-process handles before stopping its fixture/product children and deleting only its own temporary directory. If an owned child cannot be closed, cleanup retains the directory for safety. The unrelated DesktopPuppy instance, user data, and neighboring temp files are never cleaned up.
