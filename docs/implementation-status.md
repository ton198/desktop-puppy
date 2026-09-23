# Desktop Puppy Implementation Status

> This file is the handoff point for future conversations. Read it before changing code.

**Last updated:** 2026-09-23
**Latest verified step:** Merged `main` passes 10 unit tests, 4 Windows UI tests, and Release build with 0 warnings/0 errors.
**Current phase:** Phase 3 in progress — interaction controller and desktop UI automation verification
**Current branch:** `main`
**Worktree:** `C:\Users\ton20\Documents\works\projects\desktop-puppy`
**Milestone commit:** merged to `main` at `3ec582b` (interaction controller and stabilized desktop UI automation tests)
**Base commit:** `c609a5f`

## Completed

- [x] Reviewed the PRD, design spec, and implementation plan.
- [x] Confirmed the repository initially contained documentation only.
- [x] Created an isolated implementation worktree and `codex/desktop-puppy-foundation` branch.
- [x] Installed .NET SDK `10.0.401` and confirmed Windows Desktop runtime availability.
- [x] Added `global.json` to pin the SDK family with `latestFeature` roll-forward.
- [x] Created `DesktopPuppy.sln`, the WPF app project, and the xUnit test project.
- [x] Wrote and observed the RED smoke test for `ApplicationInfo.ProductName`.
- [x] Wrote and observed the RED hit-cooldown test for `PetInteractionPolicy`.
- [x] Implemented `ApplicationInfo.ProductName` as `Desktop Puppy`.
- [x] Implemented the transparent, always-on-top offline placeholder pet window.
- [x] Implemented delayed single-click local lines using the Windows double-click interval.
- [x] Implemented double-click hit reaction with a one-second local cooldown.
- [x] Implemented right-click local bubble, bubble close button, drag movement, and a visible exit button.
- [x] Added `IClock`/`SystemClock` and immutable offline settings defaults.
- [x] Added pure monitor selection and 20-pixel-visible pet placement clamping.
- [x] Updated `.gitignore` and `README.md` with the current phase and resume entry point.
- [x] Verified the new core/placement tests pass (4 focused tests).


## Test coverage audit — 2026-09-23

Current automated test count: **10 unit tests passed, 0 failed; 4 Windows UI tests passed, 0 failed**.

Covered by unit tests:

- `ApplicationInfo.ProductName` smoke contract;
- hit cooldown policy;
- default settings policy;
- injectable clock contract;
- pet position clamping with a minimum visible edge;
- missing-monitor fallback to the primary monitor;
- delayed single-click emission;
- double-click cancellation and hit emission;
- drag start/completion with click suppression;
- right-click event emission.

Covered by the separate Windows UI test project:

- visible WPF window and close button through UI Automation;
- right-click speech bubble and bubble close;
- double-click hit response;
- drag movement without closing the pet.

Verified outside unit tests:

- Release solution compilation with 0 warnings and 0 errors;
- clean Windows UI automation run with 4 passed and 0 failed;
- application process starts and remains alive during the earlier startup smoke check;
- `git diff --check` is clean.

Not yet verified:

- actual WPF transparency, topmost behavior, DPI scaling, multi-monitor movement, and monitor removal;
- single-instance behavior;
- tray, startup registration, fullscreen detection, foreground hooks, SQLite, usage statistics, AI, reports, settings persistence, and data deletion;
- the full PRD acceptance matrix.

The 14 automated tests cover the current offline interaction milestone only; they must not be described as full MVP or full PRD coverage.

## Current milestone contents

The current implementation remains offline-only. It intentionally does **not** include:

- SQLite or local usage history;
- Win32 foreground hooks, fullscreen detection, native monitor enumeration, or tray;
- DeepSeek/Jev, API keys, network calls, or AI fallback;
- real animation assets or continuous animation loops;
- settings persistence or startup registration.

`MainWindow.xaml` remains a compatibility placeholder. The actual startup surface is `src/DesktopPuppy/Pet/PetWindow.xaml`.

## Verification commands

Run from the worktree with .NET 10 SDK:

```powershell
$env:DOTNET_CLI_HOME = "C:\Users\ton20\Documents\works\projects\desktop-puppy\.dotnet-home"
& "C:\Program Files\dotnet\dotnet.exe" test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --no-restore --configuration Release
& "C:\Program Files\dotnet\dotnet.exe" build DesktopPuppy.sln --configuration Release --no-restore
$env:DESKTOP_PUPPY_EXE = "C:\Users\ton20\.codex\worktrees\desktop-puppy-foundation\desktop-puppy\src\DesktopPuppy\bin\Release\net10.0-windows\DesktopPuppy.exe"
& "C:\Program Files\dotnet\dotnet.exe" test tests/DesktopPuppy.UiTests/DesktopPuppy.UiTests.csproj --configuration Release --no-restore --filter "Category=Ui"
& "C:\Program Files\dotnet\dotnet.exe" run --project src/DesktopPuppy/DesktopPuppy.csproj --configuration Release --no-build
```

Observed on 2026-09-23 for the current milestone:

```text
Unit tests: 10 passed, 0 failed
Windows UI tests: 4 passed, 0 failed
Release build: 0 warnings, 0 errors
```

The UI test project must be run on an interactive Windows desktop because it launches and controls a real WPF process. The current milestone is verified, but the broader PRD acceptance matrix remains incomplete.

## Important environment notes

- `dotnet` was not available from the existing PowerShell PATH when this work started. Use `C:\Program Files\dotnet\dotnet.exe` or add that directory to PATH.
- The machine's default NuGet configuration contains a missing Visual Studio offline source. Restore with the explicit source `https://api.nuget.org/v3/index.json` until the project adds a local NuGet configuration.
- The original checkout has uncommitted documentation edits that predate this worktree. Do not overwrite them; reconcile or cherry-pick deliberately later.
- The managed worktree is outside the original project directory. Future conversations should use this worktree/branch or inspect this status file before making changes.

## Next resume action

1. Read this file and confirm the worktree/branch.
2. Add the next failing tests for the native window boundary and single-instance policy.
3. Connect `PetPlacementPolicy` to a monitor adapter without moving Win32 calls into the WPF view.
4. Keep the app offline and runnable while adding the native boundary.
5. Update this file immediately after each verified milestone.


## Post-merge verification — 2026-09-23

- The first merged-main verification stopped before tests because the original checkout had no `obj/project.assets.json` files.
- Restored the solution and separate UI-test project from `https://api.nuget.org/v3/index.json`.
- Merged-main verification then passed: **10 unit tests, 4 Windows UI tests, Release build with 0 warnings and 0 errors**.
- The original unrelated README/PRD document edits and `.dotnet-home/` remain uncommitted and were intentionally preserved.
## Post-merge verification note — 2026-09-23

- The first post-commit full verification exposed an intermittent UI-harness failure: one of four UI cases timed out; the failing case varied between double-click and drag.
- Root cause: the harness used fixed sleeps after `SendInput` and could move the pointer before WPF had acquired native mouse capture.
- [x] Replaced the drag delay with a `GetGUIThreadInfo` condition wait for the pet window's native mouse capture.
- [x] Corrected the Win32 entry-point mapping to `GetGUIThreadInfo`.
- [x] Repeated the complete UI suite 5 times after the fix: **20 passed, 0 failed**.
## Desktop UI automation verification — 2026-09-23

- [x] Corrected the UI harness P/Invoke mapping to `GetWindowRect`.
- [x] Confirmed the transparent window must be clicked on the visible pet graphic; the geometric center can hit the underlying desktop window.
- [x] Confirmed `SpeechBubble` is a WPF `Border` without a UI Automation peer; tests target the visible `SpeechBubbleText` peer and `CloseBubbleButton` instead.
- [x] Added a small incremental movement to the drag injection so the WPF capture/`DragMove()` path receives a realistic mouse sequence.
- [x] Verified clean UI automation with diagnostics disabled: **4 passed, 0 failed** per run; after stabilizing native mouse capture, 5 consecutive runs also passed (**20/20**).
  - window automation surface and close button;
  - right-click bubble and bubble close;
  - double-click hit response;
  - drag movement without closing the pet.
- [ ] The UI test project remains intentionally separate from the default unit-test run because it launches a real desktop process and requires an interactive Windows desktop.
## Desktop automation diagnosis — 2026-09-23

Root cause investigation result: the WPF application does create a real visible top-level window, and Windows UI Automation can see it. The current failure is in the discovery/binding path used by the available CUA wrapper, plus the absence of a UI automation test harness in the xUnit project.

Evidence:

- A running `DesktopPuppy` process was found.
- `Get-Process` reported `MainWindowHandle=0` and an empty `MainWindowTitle`, so PowerShell's main-window heuristic does not identify the WPF pet window.
- Native `EnumWindows` found a visible top-level window titled `Desktop Puppy` for the same process.
- Windows UI Automation resolved that HWND as `ControlType.Window`, `Name=Desktop Puppy`, `IsOffscreen=False`.
- Windows UI Automation found one visible button with `Name=×` and `AutomationId=CloseButton`.
- The current CUA runtime returned no app inventory entry and its runtime object did not expose the documented `listWindows` or `getApp` methods, so it could not bind to the known HWND through the current wrapper.

Contributing factors in the current production window:

- `AllowsTransparency=True`;
- `WindowStyle=None`;
- `ShowInTaskbar=False`;
- `Topmost=True`.

These are appropriate for a desktop pet but make taskbar/process-based discovery less reliable. They are not evidence that the window is absent: native enumeration and UI Automation both found it.

Next testability work:

1. Add a separate Windows UI Automation integration harness that enumerates the pet window by process/title or an explicit test handle, instead of relying only on the CUA app inventory.
2. Add `AutomationProperties.AutomationId`/`Name` to the pet surface, bubble, bubble-close button, and interaction state so UI tests can target stable elements.
3. Keep pure interaction-controller tests in xUnit and use UI Automation only for window creation, hit-test routing, drag/click smoke checks, and close behavior.
4. Consider an `--automation` launch mode only if the harness cannot interact reliably with the transparent production window; do not weaken normal production window behavior just to satisfy discovery.

## Foreground usage vertical slice — 2026-09-23

- [x] Added `ForegroundWindowSnapshot`, `IForegroundWindowWatcher`, and a dedicated-thread `WinEventForegroundWatcher` around `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`.
- [x] Added `ISingleInstanceGuard` and a named-Mutex `SingleInstanceGuard` with second-instance activation signaling; Mutex ownership and release stay on one dedicated thread.
- [x] Added `ForegroundSessionTracker` with application switching, Desktop Puppy exclusion, and lock/sleep/hibernate/shutdown boundaries.
- [x] Added `WindowTitleDebouncer` with a two-second stability window and upsert-by-interval identity.
- [x] Added `UsageCollector` to connect the native watcher, session tracker, title debouncer, and sink contract without putting Win32 calls in the WPF view.
- [x] Added `UsageSqliteStore` for `usage_sessions` and `window_title_intervals`, with parameterized SQL, UTC round-trips, idempotent schema creation, and deterministic connection cleanup.
- [x] Wired usage tracking into `App` startup/shutdown; title advancement runs on a background `System.Threading.Timer`, not the WPF Dispatcher.
- [x] Pinned `SQLitePCLRaw.lib.e_sqlite3` to `2.1.13`; `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages for the application project.
- [x] Verified **33 unit tests passed**, **4 Windows UI tests passed**, and **Release build completed with 0 warnings / 0 errors**.

This slice proves the local usage path, but it is not the complete statistics subsystem yet. Still not verified or implemented: real lock/sleep power-event adapters, daily aggregation, 15-day retention, monitor/DPI placement, fullscreen detection, tray, startup registration, and the full PRD acceptance matrix.

### Next resume action

1. Add the native power/session boundary adapter and feed lock, sleep, hibernate, unlock, and shutdown events into `UsageCollector.OnSessionBoundary`.
2. Add daily aggregation and local-day splitting on top of persisted sessions and title intervals.
3. Add 15-day retention while preserving settings and future encrypted API-key storage.
4. Add monitor/DPI/fullscreen adapters without moving native calls into WPF views.
