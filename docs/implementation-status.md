# Desktop Puppy Implementation Status

> This file is the handoff point for future conversations. Read it before changing code.

**Last updated:** 2026-09-25
**Latest verified step:** Merged the Windows session/power integration and lock-screen Hook suppression to `main` (`cca4eb1`); 79 unit tests and 4 Windows UI tests pass, and Release build has 0 warnings / 0 errors. The initial UI-suite run had one window-discovery timeout; the isolated test and a complete rerun both passed.
**Current phase:** Phase 4 in progress — foreground collection, session/power notification wiring, local-day aggregation, retention policy, and DPI geometry core
**Current branch:** `main`
**Worktree:** `C:\Users\ton20\Documents\works\projects\desktop-puppy`
**Latest integration commit:** `cca4eb1` (`feat: integrate Windows session and power events`)
**Base commit:** `3c2c116`

## Completed

- [x] Reviewed the PRD, design spec, and implementation plan.
- [x] Confirmed the repository initially contained documentation only.
- [x] Created the WPF app, unit-test project, and separate Windows UI-test project.
- [x] Installed .NET SDK `10.0.401` and confirmed Windows Desktop runtime availability.
- [x] Added `global.json` to pin the SDK family with `latestFeature` roll-forward.
- [x] Implemented the transparent, always-on-top offline placeholder pet window.
- [x] Implemented delayed single-click local lines using the Windows double-click interval.
- [x] Implemented double-click hit reaction with a one-second local cooldown.
- [x] Implemented right-click local bubble, bubble close button, drag movement, and a visible exit button.
- [x] Added `IClock`/`SystemClock`, immutable offline settings defaults, pure monitor selection, and 20-pixel-visible placement clamping.
- [x] Added `ForegroundWindowSnapshot`, `IForegroundWindowWatcher`, and a dedicated-thread `WinEventForegroundWatcher` around `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`.
- [x] Added `ISingleInstanceGuard` and a named-Mutex `SingleInstanceGuard` with second-instance activation signaling; Mutex ownership and release stay on one dedicated thread.
- [x] Wired the single-instance guard into `App` startup, second-instance activation, and shutdown cleanup.
- [x] Added `ForegroundSessionTracker` with application switching, Desktop Puppy exclusion, and lock/sleep/hibernate/shutdown boundary APIs.
- [x] Added `WindowTitleDebouncer` with a two-second stability window and upsert-by-interval identity.
- [x] Added `UsageCollector` to connect the native watcher, session tracker, title debouncer, and sink contract without putting Win32 calls in the WPF view.
- [x] Added `UsageSqliteStore` for `usage_sessions` and `window_title_intervals`, with parameterized SQL, UTC round-trips, idempotent schema creation, and deterministic connection cleanup.
- [x] Wired usage tracking into `App` startup/shutdown; title advancement runs on a background `System.Threading.Timer`, not the WPF Dispatcher.
- [x] Added `SystemEventsSessionPowerEventSource` for lock/unlock and generic suspend/resume notifications; it routes into `SessionPowerEventRouter` and `UsageCollector`, then unsubscribes before collector disposal.
- [x] Suppresses foreground Hook callbacks after lock/suspend boundaries; resume or unlock re-samples the current foreground window, and resume while locked waits until unlock.
- [x] Pinned `SQLitePCLRaw.lib.e_sqlite3` to `2.1.13`; the application vulnerability scan reports no vulnerable packages.

## Historical test coverage audit — 2026-09-23

At this historical checkpoint, the automated test count was **33 unit tests passed, 0 failed; 4 Windows UI tests passed, 0 failed**.

Covered by unit tests:

- existing application, settings, placement, and interaction contracts;
- single-instance guard acquisition and second-instance activation signaling;
- foreground session opening, application switching, repeated foreground events, and Desktop Puppy exclusion;
- lock, sleep, hibernate, and shutdown session boundaries;
- two-second title stability, title replacement, duplicate suppression, and abandoned pending titles;
- UsageCollector composition of watcher, session tracker, and title debouncer;
- SQLite session round-trip, title interval upsert, and idempotent initialization.

Covered by the separate Windows UI test project:

- visible WPF window and close button through UI Automation;
- right-click speech bubble and bubble close;
- double-click hit response;
- drag movement without closing the pet;
- startup with the foreground Hook/SQLite path and single-instance guard enabled.

Verified outside unit tests:

- Release solution compilation with 0 warnings and 0 errors;
- clean Windows UI automation run with 4 passed and 0 failed after usage tracking and single-instance startup integration;
- `git diff --check` is clean;
- `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages for the application project.

At this 2026-09-23 checkpoint, the following had not yet been verified or implemented:

- dedicated two-process manual assertion that the second launch activates the first visible pet window;
- actual WPF transparency, topmost behavior, DPI scaling, multi-monitor movement, monitor removal, and native monitor enumeration;
- native lock/sleep/hibernate/power-event adapter feeding the boundary APIs;
- daily aggregation, local-midnight session splitting, and 15-day retention;
- tray, startup registration, fullscreen detection, AI, reports, settings persistence, and data deletion;
- the full PRD acceptance matrix.

At that checkpoint, the **37 automated tests** covered only the interaction and foreground-usage milestone; they were not full MVP or full PRD coverage.

## Original foreground-usage milestone contents

At the original foreground-usage checkpoint (2026-09-23), implementation was offline-only and included:

- transparent WPF pet interaction surface;
- WinEvent foreground-window adapter;
- in-memory session/title tracking with a two-second title debounce;
- local SQLite usage sessions and title intervals;
- single-instance startup guard and activation signaling.

At that checkpoint, the milestone intentionally did **not** include:

- native power/session event adapters;
- daily aggregation, retention, monitor/DPI/fullscreen adapters, or tray;
- DeepSeek/Jev, API keys, network calls, or AI fallback;
- real animation assets or continuous animation loops;
- settings persistence or startup registration.

`MainWindow.xaml` remains a compatibility placeholder. The actual startup surface is `src/DesktopPuppy/Pet/PetWindow.xaml`.

## Verification commands

Run from the main checkout with .NET 10 SDK:

```powershell
$env:DOTNET_CLI_HOME = "C:\Users\ton20\Documents\works\projects\desktop-puppy\.dotnet-home"
& "C:\Program Files\dotnet\dotnet.exe" test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --no-restore --configuration Release
& "C:\Program Files\dotnet\dotnet.exe" build DesktopPuppy.sln --configuration Release --no-restore
$env:DESKTOP_PUPPY_EXE = "C:\Users\ton20\Documents\works\projects\desktop-puppy\src\DesktopPuppy\bin\Release\net10.0-windows\DesktopPuppy.exe"
& "C:\Program Files\dotnet\dotnet.exe" test tests/DesktopPuppy.UiTests/DesktopPuppy.UiTests.csproj --configuration Release --no-restore --filter "Category=Ui"
& "C:\Program Files\dotnet\dotnet.exe" list src/DesktopPuppy/DesktopPuppy.csproj package --vulnerable --include-transitive
```

Observed on **September 23, 2026** for this milestone:

```text
Unit tests: 33 passed, 0 failed
Windows UI tests: 4 passed, 0 failed
Release build: 0 warnings, 0 errors
Application dependency vulnerability scan: no vulnerable packages
```

The UI test project must be run on an interactive Windows desktop because it launches and controls a real WPF process. The broader PRD acceptance matrix remains incomplete.

## Important environment notes

- `dotnet` was not available from the existing PowerShell PATH when this work started. Use `C:\Program Files\dotnet\dotnet.exe` or add that directory to PATH.
- The machine's default NuGet configuration contains a missing Visual Studio offline source. Restore with the explicit source `https://api.nuget.org/v3/index.json` until the project adds a local NuGet configuration.
- The main checkout retains local uncommitted README, plan, and design-spec edits, plus the untracked `.dotnet-home/` directory. Preserve these local items unless explicitly asked to change them.
- The feature worktree at `C:\Users\ton20\.codex\worktrees\foreground-usage-slice\desktop-puppy` remains available, but its changes are merged into `main`. This handoff now describes the main checkout.

## Historical next resume action (superseded by later checkpoints)

1. Add the native power/session boundary adapter and tests for lock, sleep, hibernate, unlock, and shutdown.
2. Add daily aggregation with local-midnight splitting on persisted sessions and title intervals.
3. Add 15-day retention while preserving settings and future encrypted API-key storage.
4. Add monitor/DPI/fullscreen adapters without moving Win32 calls into WPF views.
5. Add a dedicated two-process single-instance UI/integration check.
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
- [x] Wired the single-instance guard into `App` startup, second-instance activation, and shutdown cleanup.
- [x] Added `ForegroundSessionTracker` with application switching, Desktop Puppy exclusion, and lock/sleep/hibernate/shutdown boundaries.
- [x] Added `WindowTitleDebouncer` with a two-second stability window and upsert-by-interval identity.
- [x] Added `UsageCollector` to connect the native watcher, session tracker, title debouncer, and sink contract without putting Win32 calls in the WPF view.
- [x] Added `UsageSqliteStore` for `usage_sessions` and `window_title_intervals`, with parameterized SQL, UTC round-trips, idempotent schema creation, and deterministic connection cleanup.
- [x] Wired usage tracking into `App` startup/shutdown; title advancement runs on a background `System.Threading.Timer`, not the WPF Dispatcher.

- [x] Pinned `SQLitePCLRaw.lib.e_sqlite3` to `2.1.13`; `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages for the application project.
- [x] Verified **33 unit tests passed**, **4 Windows UI tests passed**, and **Release build completed with 0 warnings / 0 errors**.

At the original 2026-09-23 foreground-usage checkpoint, this slice proved the local usage path but was not the complete statistics subsystem. At that time, real lock/sleep power-event adapters, daily aggregation, 15-day retention, monitor/DPI placement, fullscreen detection, tray, startup registration, and the full PRD acceptance matrix remained unfinished; the manual two-process activation check was also still outstanding.

### Historical next resume action — 2026-09-23 (superseded by later checkpoints)

1. Add the native power/session boundary adapter and feed lock, sleep, hibernate, unlock, and shutdown events into `UsageCollector.OnSessionBoundary`.
2. Add daily aggregation and local-day splitting on top of persisted sessions and title intervals.
3. Add 15-day retention while preserving settings and future encrypted API-key storage.
4. Add monitor/DPI/fullscreen adapters without moving native calls into WPF views.
## Pre-native-event checkpoint — 2026-09-24

The testable usage-statistics and DPI core slice is implemented on `codex/foreground-usage-slice`.

- Added 8 session/power event-router tests for normalized lock, suspend, hibernate, shutdown, unlock/resume behavior, unknown events, and repeated boundaries. These do **not** exercise native Windows notification delivery.
- Added 9 daily-aggregation tests for same-day usage, local-midnight splitting, exact-midnight ends, injected timezone, process totals, title detail, open intervals capped by `asOfUtc`, and Eastern spring/fall DST transitions.
- Added 10 SQLite retention tests for the 15-local-date cutoff, exact cutoff inclusion, deleting expired rows, clipping crossing intervals, preserving open titles, idempotency, unrelated tables, DST calendar arithmetic, and UTC round-trips.
- Added 9 executed DPI/placement test cases (including the three scale rows in one round-trip theory) for conversion, precision, negative-origin secondary monitors, and preserving a 20-physical-pixel edge across scales.
- Existing 33 unit tests remain green: **69 unit tests passed, 0 failed**.

Fresh verification on 2026-09-24:

```text
Unit tests: 69 passed, 0 failed
Windows UI tests: 4 passed, 0 failed
Release solution build: 0 warnings, 0 errors
```

The Windows UI suite verifies the existing WPF interaction/startup behavior only. Still required before claiming complete system integration:

1. Manually verify that real Windows lock/unlock and suspend/resume notifications reach the app; automated unit tests use an injected provider and do not exercise live OS delivery.
2. Invoke and schedule the 15-local-date cleanup from application runtime; `UsageSqliteStore.PruneBefore` currently exists but is not called by `App`.
3. Connect daily aggregation to a report/statistics consumer.
4. Connect DPI conversion to native monitor work-area enumeration and the real WPF placement path; test mixed-DPI monitor transitions and monitor removal on Windows.
5. Verify two-process single-instance activation with a dedicated Windows integration check.

At this pre-native-event checkpoint, 69 unit tests covered the interaction, foreground-usage, aggregation, retention-store, and pure geometry core. The later session/power integration and its 79-test verification are recorded in the following section.
## Native Windows session/power event integration and locked-Hook suppression — 2026-09-25

- [x] Added `SystemEventsSessionPowerEventSource` for Windows session lock/unlock and power suspend/resume notifications.
- [x] Connected the source to `App` startup/shutdown and the existing event router / `UsageCollector` path; the source is unsubscribed before the collector is disposed.
- [x] Added explicit `SessionBoundary.Suspend`. Windows `PowerModes.Suspend` is recorded as a generic suspend boundary; this source does not assert whether Windows used sleep or hibernate.
- [x] Added a foreground snapshot refresh on resume and unlock. Resume while the session is still locked is intentionally ignored until unlock, preventing a locked interval from reopening usage tracking.
- [x] Added unit coverage for event normalization, irrelevant notifications, unsubscription, routing, lock-state gating, and resumed collection. These tests use an injected notification provider and do **not** prove that the live Windows desktop delivered the native notifications.

### Per-test purpose, coverage, and target effect

#### `SystemEventsSessionPowerEventSourceTests`

1. `Session_lock_and_unlock_notifications_are_normalized_with_the_current_time` (two theory rows)
   - **Purpose:** Catch reversed or incorrect mapping of lock/unlock notifications, or timestamps not taken from the injected clock.
   - **Coverage:** `SessionLock`, `SessionUnlock`, normalized event value, and event timestamp.
   - **Target effect:** Each notification reaches the consumer as its corresponding normalized event at the clock's current UTC time.
2. `Unrelated_session_switch_reasons_do_not_emit_usage_events`
   - **Purpose:** Prevent session-connect notifications from being mistaken for a lock boundary.
   - **Coverage:** A non-lock/unlock reason (`ConsoleConnect`).
   - **Target effect:** No normalized event is emitted for unrelated session-switch reasons.
3. `Suspend_and_resume_notifications_are_normalized` (two theory rows)
   - **Purpose:** Catch swapped or missing power-mode mappings.
   - **Coverage:** `PowerModes.Suspend` and `PowerModes.Resume`, including timestamps.
   - **Target effect:** Suspend and resume reach the router as distinct normalized events.
4. `Power_status_change_does_not_end_or_restart_a_foreground_session`
   - **Purpose:** Prevent battery or AC-status changes from closing or refreshing a usage session.
   - **Coverage:** `PowerModes.StatusChange`.
   - **Target effect:** A power-status-only notification emits no session/power event.
5. `Disposing_the_source_unsubscribes_it_from_later_system_notifications`
   - **Purpose:** Catch leaked static event subscriptions that could call a disposed collector during app shutdown.
   - **Coverage:** Session and power notifications raised after source disposal.
   - **Target effect:** Neither later notification reaches the consumer.

#### `SessionPowerEventRouterTests`

1. `Locked_event_routes_lock_boundary_at_event_time`
   - **Purpose:** Catch a lost lock boundary or a boundary timestamp replaced with processing time.
   - **Coverage:** Normalized lock event and supplied timestamp.
   - **Target effect:** Exactly one `Lock` boundary is forwarded at the event time.
2. `Suspend_event_routes_generic_suspend_boundary`
   - **Purpose:** Ensure the platform's generic suspend signal closes usage without falsely labeling it as hibernate.
   - **Coverage:** Normalized suspend event.
   - **Target effect:** Exactly one generic `Suspend` boundary is forwarded.
3. `Hibernate_event_routes_hibernate_boundary`
   - **Purpose:** Preserve routing for an explicitly identified hibernate event from a future or more specific provider.
   - **Coverage:** An already-normalized `Hibernate` event; this does not claim the current Windows source distinguishes hibernate.
   - **Target effect:** The explicit event maps to the `Hibernate` boundary.
4. `Shutdown_event_routes_shutdown_boundary`
   - **Purpose:** Protect the existing shutdown close behavior.
   - **Coverage:** Normalized shutdown event and its event time.
   - **Target effect:** Exactly one `Shutdown` boundary is forwarded.
5. `Unlock_requests_a_foreground_refresh_without_creating_a_boundary`
   - **Purpose:** Ensure unlocking resumes foreground discovery without inventing a usage boundary.
   - **Coverage:** Unlock callback and boundary callback counts.
   - **Target effect:** One foreground refresh is requested and no boundary is emitted.
6. `Resume_requests_a_foreground_refresh_when_the_session_is_unlocked`
   - **Purpose:** Prevent tracking from remaining stopped when the foreground window did not change during suspend.
   - **Coverage:** Suspend followed by resume while not locked.
   - **Target effect:** The suspend boundary is preserved and one fresh foreground sample is requested on resume.
7. `Resume_while_locked_waits_until_unlock_before_refreshing_foreground`
   - **Purpose:** Prevent a resume into the lock screen from reopening a usage session.
   - **Coverage:** Lock → suspend → resume while locked → unlock sequence.
   - **Target effect:** Resume does not refresh while locked; unlock performs exactly one refresh, with no extra boundaries.
8. `Unknown_session_event_is_ignored`
   - **Purpose:** Make future or malformed enum values fail closed.
   - **Coverage:** An undefined normalized event value.
   - **Target effect:** No boundary or refresh callback runs.
9. `Repeated_boundary_without_open_session_does_not_emit_duplicate_close`
   - **Purpose:** Protect idempotent session closure when Windows emits repeated boundary notifications.
   - **Coverage:** Two lock events with no foreground session remaining after the first.
   - **Target effect:** Only the original foreground session is closed once.

#### `UsageCollectorTests` additions

1. `Resume_refreshes_the_current_window_and_starts_a_new_usage_session`
   - **Purpose:** Catch a broken handoff from resume routing to the foreground watcher / collector.
   - **Coverage:** Same foreground process before and after suspend, with explicit event times.
   - **Target effect:** Usage is split at suspend; a new session starts at resume even though the active window did not change.
2. `Resume_while_locked_does_not_count_the_lock_screen_and_unlock_refreshes_usage`
   - **Purpose:** Prevent foreground Hook callbacks during a locked interval from reopening usage, and ensure collection recovers after unlock.
   - **Coverage:** Lock → Hook reports the Windows lock-screen process → suspend → resume while locked → restore the application as current foreground → unlock refresh → shutdown.
   - **Target effect:** The lock-screen callback creates no usage session; unlock takes one fresh sample and resumes tracking the actual application.

### Verification boundary

Fresh verification on 2026-09-25:

```text
Unit tests: 79 passed, 0 failed
Windows UI tests: 4 passed, 0 failed
Release solution build: 0 warnings, 0 errors
git diff --check: clean
```

The automated adapter tests validate mapping and lifecycle with an injected notification provider. The Windows UI suite validates ordinary app startup/interactions, not delivery of a real lock or power transition. On 2026-09-25, the first full UI run had one window-discovery timeout; the affected case passed alone and the complete 4-test suite passed on rerun. A manual Windows check using lock/unlock and sleep/resume is still required to confirm live OS notification delivery; hibernate-vs-sleep classification is intentionally not claimed by this source.
