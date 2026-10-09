# Desktop Puppy Implementation Status

> This file is the handoff point for future conversations. Read it before changing code.

**Last updated:** 2026-10-08 (45-degree left/right edge docking integrated on local main; previous checkpoint `12a6f8c`)
**Latest verified step:** Edge docking Release solution build: 0 errors / 2 existing WFO0003 warnings; separate UI-project build: 0 errors / 0 warnings. Complete deterministic suite: 269 passed; complete `Category=Ui` suite: 16 passed, 0 failed, 0 skipped (35 seconds). See `docs/verification/2026-10-08-edge-docking.md`.
**Current phase:** Phase 4 in progress — offline statistics, monitor/DPI placement, tray, Snoopy animations, right-click AI with persistent AI settings, fullscreen suppression and edge docking are implemented. Reports, AI greetings/activities, complete settings and startup registration remain future features; real Shell/OS/multi-monitor hardware acceptance remains pending.
**Current handoff:** The user approved the 45-degree edge sprites, accepted the running implementation, and requested local integration. Runtime, assets, tests and verification records are included in the local main integration commit. No push performed. Next: remaining Windows hardware/Shell checklist, then AI greetings/activities.

**AI integration — 2026-10-08:** The right-click AI worktree was fast-forward merged into local main and verified at `12a6f8c`; no push performed. Historical worktree checkpoints below describe their state at the time.
**Current verification branch:** `main` (tray branch `codex/wpf-tray-icon` fast-forward-merged locally)
**Current verification worktree:** `C:\Users\ton20\Documents\works\projects\desktop-puppy` (Task 7 was verified in the isolated worktree noted below)
**Task 7 starting commit:** `7a22be97aa92fc69d1bb2128bc50e85c53c31640`
**Latest tray integration commit on main:** `b1cfbda330c99267871baf92a42d3b484c4d5fe1` (`docs: record tray final review verification`; fast-forward target; not pushed)
**Original implementation base (historical):** `3c2c116`

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
- [x] Added `UsageStatisticsQueryService` to combine a consistent live-collector snapshot with persisted sessions/title intervals, clip data to the snapshot time, and produce local-date totals ranked by application and title.
- [x] Added the separate usage-statistics window and pet entry point; the view distinguishes a valid empty day from unavailable data, supports the retained 15 local dates and application/title drill-down, and refreshes on activation and every 30 seconds.
- [x] Bound UI Automation names to the actual total-duration and error text so tests and accessibility clients can read the rendered values.
- [x] Connected 15-local-date retention to startup, a background daily-date check, and resume; cleanup runs once per local date, clips boundary-crossing intervals, and retries after failures.
- [x] Merged feature branch `codex/usage-statistics-window-retention` at `44274d4` into `main` as merge commit `2aa0fd7`; no push was performed.
- [x] Verified the merged `main`: **101 unit tests passed**, **11 Windows UI tests passed**, and the Release solution build completed with **0 warnings / 0 errors**.

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

## Daily usage statistics, 15-date retention, and statistics window — 2026-09-26

### Delivered behavior

- The statistics window is a separate, reusable window opened from the pet; it does not replace or resize the pet. Reopening reloads current data.
- Daily totals are computed from persisted usage plus a single live-session snapshot. Sessions and titles are capped at the same `AsOfUtc`, split/aggregated by the selected local date, grouped and ranked by application and title.
- The date selector exposes today and the previous fourteen local dates. Refreshing across local midnight advances that range, retains a still-valid selection, and moves an expired selection to the new local today.
- A valid day with no activity is displayed as a normal zero/empty state. Storage/query failures are shown as unavailable, not fabricated as zero usage.
- While open, the statistics window refreshes on activation and every 30 seconds. Retention is scheduled at startup and checked after resume; it keeps the current fifteen local dates and clips rows that cross the cutoff.

### Test-by-test purpose, coverage, and target effect

Each listed test has an explicit purpose, coverage boundary, and target effect. Existing interaction and low-level foreground tests are recorded in the historical sections above; this inventory covers the statistics/retention path added or extended for this milestone.

#### `DailyUsageAggregatorTests` — local-date aggregation

1. `Same_day_session_contributes_full_duration_to_one_local_day`
   - **Purpose:** Catch lost or altered duration for a session contained in one day.
   - **Coverage:** A same-local-day foreground session and its full interval.
   - **Target effect:** The one date receives the complete elapsed duration.
2. `Session_crossing_local_midnight_is_split_between_both_dates`
   - **Purpose:** Prevent overnight use from being assigned wholly to its start date.
   - **Coverage:** One session spanning two local dates.
   - **Target effect:** Each date receives only its own portion of the session.
3. `Session_ending_at_midnight_does_not_create_zero_duration_next_day_bucket`
   - **Purpose:** Avoid spurious rows for an interval ending exactly at midnight.
   - **Coverage:** An end instant at the next date's local midnight.
   - **Target effect:** No zero-duration entry is created for the following date.
4. `Aggregation_uses_injected_local_timezone_instead_of_UTC_date`
   - **Purpose:** Prevent UTC calendar dates from replacing the user's local reporting dates.
   - **Coverage:** A time zone whose local date differs from the UTC date.
   - **Target effect:** The session is attributed using the injected local zone.
5. `Multiple_sessions_for_same_process_are_summed_without_merging_other_processes`
   - **Purpose:** Keep process totals grouped correctly across separate foreground intervals.
   - **Coverage:** Multiple sessions from one process and a second process.
   - **Target effect:** Same-process durations sum together while different process totals remain separate.
6. `Title_intervals_split_at_midnight_and_remain_separate_by_title`
   - **Purpose:** Keep title detail consistent with daily application totals.
   - **Coverage:** Title intervals crossing local midnight and distinct titles.
   - **Target effect:** Each title is split by date without conflating title names.
7. `Open_title_interval_is_capped_at_explicit_as_of_time`
   - **Purpose:** Prevent an ongoing title interval from accruing beyond the report snapshot.
   - **Coverage:** An open interval and an injected `asOfUtc` value.
   - **Target effect:** The reported title duration ends exactly at `asOfUtc`.
8. `Spring_forward_day_uses_elapsed_utc_duration_and_one_local_date`
   - **Purpose:** Avoid losing or inventing elapsed time on the spring DST transition.
   - **Coverage:** A session crossing the skipped local hour.
   - **Target effect:** Elapsed UTC duration is correct and remains on the intended local date.
9. `Fall_back_day_counts_repeated_local_hour_once_per_elapsed_interval`
   - **Purpose:** Avoid double counting the repeated wall-clock hour during fall DST.
   - **Coverage:** A session spanning the repeated local hour.
   - **Target effect:** Duration reflects elapsed time once, with local-date attribution intact.

#### `UsageSqliteStoreRetentionTests` — database retention and clipping

1. `Retention_cutoff_is_midnight_of_oldest_of_fifteen_local_dates`
   - **Purpose:** Keep the retention boundary aligned to fifteen calendar dates rather than a fixed-hour duration.
   - **Coverage:** Policy cutoff with an injected local time zone.
   - **Target effect:** The cutoff is the oldest retained local midnight converted to UTC.
2. `Session_ending_exactly_at_cutoff_is_deleted_but_session_starting_at_cutoff_is_kept`
   - **Purpose:** Define the exact inclusive/exclusive boundary and prevent off-by-one retention.
   - **Coverage:** Sessions ending at and starting at the cutoff instant.
   - **Target effect:** Expired sessions are removed while sessions beginning at cutoff are retained.
3. `Sessions_and_closed_titles_wholly_older_than_cutoff_are_deleted`
   - **Purpose:** Remove fully expired persisted usage data.
   - **Coverage:** Old sessions and closed title intervals entirely before cutoff.
   - **Target effect:** Neither expired record remains in storage.
4. `Session_crossing_cutoff_is_clipped_and_keeps_only_retained_duration`
   - **Purpose:** Preserve the retained portion of a session instead of dropping useful data.
   - **Coverage:** One session that starts before and ends after cutoff.
   - **Target effect:** Its start is moved to cutoff and only its retained duration remains.
5. `Closed_title_interval_crossing_cutoff_is_clipped_without_changing_its_end`
   - **Purpose:** Retain valid title detail while removing its expired portion.
   - **Coverage:** Closed title interval crossing cutoff.
   - **Target effect:** Start is clipped to cutoff and original end remains unchanged.
6. `Open_title_interval_crossing_cutoff_is_clipped_and_remains_open`
   - **Purpose:** Ensure cleanup does not accidentally close a currently active title interval.
   - **Coverage:** Open title interval beginning before cutoff.
   - **Target effect:** Start is clipped while `EndedAt` remains null.
7. `Repeated_retention_cleanup_is_idempotent`
   - **Purpose:** Make repeated startup/resume/date checks safe.
   - **Coverage:** Applying the same cutoff more than once.
   - **Target effect:** Subsequent pruning leaves retained data unchanged.
8. `Retention_cleanup_does_not_modify_unrelated_tables`
   - **Purpose:** Constrain cleanup to usage-owned tables.
   - **Coverage:** An unrelated table alongside usage records.
   - **Target effect:** Unrelated table data is preserved.
9. `Retention_cutoff_tracks_local_calendar_across_daylight_saving_change`
   - **Purpose:** Prevent a fixed UTC offset from shifting the local-calendar retention window at DST.
   - **Coverage:** Cutoff arithmetic around a daylight-saving transition.
   - **Target effect:** Cutoff follows local calendar midnight using the correct offset.
10. `Clipped_timestamps_round_trip_as_utc_instants`
    - **Purpose:** Protect timestamp kind/offset semantics during SQLite persistence.
    - **Coverage:** A clipped record written and read back from SQLite.
    - **Target effect:** Stored timestamps return as the same UTC instants.

#### `UsageStatisticsQueryServiceTests` — persisted plus live report query

1. `Query_counts_active_session_only_through_snapshot_as_of`
   - **Purpose:** Show current foreground usage without requiring the session to close first.
   - **Coverage:** Active session, clock-captured snapshot, selected local date, and application total.
   - **Target effect:** Live duration contributes only from session start through snapshot time.
2. `Query_deduplicates_active_session_when_closed_row_appears_during_read`
   - **Purpose:** Prevent double counting at the transition from a live session to a persisted row.
   - **Coverage:** Matching session IDs in live and persisted state, including a persisted end after snapshot time.
   - **Target effect:** One session contributes once and is capped at the snapshot.
3. `Query_clips_title_interval_closed_after_snapshot_as_of`
   - **Purpose:** Keep a title interval from extending the report beyond its refresh instant.
   - **Coverage:** Persisted title ending after `AsOfUtc`.
   - **Target effect:** The title duration ends at the snapshot timestamp.
4. `Query_returns_selected_local_date_with_ranked_apps_and_title_details`
   - **Purpose:** Produce the complete daily application/title hierarchy.
   - **Coverage:** Repeated process/title rows, an adjacent date, selected local date, and descending ranking.
   - **Target effect:** Only the requested date appears, grouped totals are correct, and apps/titles are ranked by duration.
5. `Query_returns_empty_daily_statistics_when_date_has_no_usage`
   - **Purpose:** Represent a valid no-activity date distinctly from a query failure.
   - **Coverage:** Empty database result for the requested date.
   - **Target effect:** The requested date returns zero total and no applications.
6. `Query_propagates_store_read_failure_for_the_view_to_report`
   - **Purpose:** Preserve storage errors so the UI can show unavailable data.
   - **Coverage:** A disposed SQLite store during query.
   - **Target effect:** The read failure propagates instead of becoming an empty report.

#### `UsageStatisticsViewModelTests` — selection, refresh, and UI state

1. `View_model_exposes_today_and_previous_fourteen_local_dates`
   - **Purpose:** Align selectable history with the retention policy.
   - **Coverage:** Fixed UTC clock near a date boundary and injected UTC+14 zone.
   - **Target effect:** Exactly fifteen consecutive local dates appear newest-first, with local today selected.
2. `Selecting_date_queries_and_displays_that_date`
   - **Purpose:** Ensure date navigation reaches the data query.
   - **Coverage:** Selecting a retained prior date and receiving its report.
   - **Target effect:** Requested, selected, and displayed dates agree.
3. `Selecting_application_exposes_only_its_title_details`
   - **Purpose:** Keep title details scoped to the chosen application.
   - **Coverage:** Two application summaries and repeated selection changes.
   - **Target effect:** Only the selected process's titles are exposed; daily total is unchanged.
4. `Older_query_completion_cannot_replace_newer_date_selection`
   - **Purpose:** Prevent a delayed earlier read from overwriting the latest user selection.
   - **Coverage:** Two controlled query tasks completed in reverse order.
   - **Target effect:** Only the newest request updates report, loading, and error state.
5. `Refreshing_after_local_midnight_advances_dates_and_keeps_retained_selection`
   - **Purpose:** Roll the date list forward without discarding a selection that remains retained.
   - **Coverage:** Injected clock crossing local midnight with a still-valid prior date selected.
   - **Target effect:** New date list is published and the valid selection is preserved.
6. `Refreshing_after_local_midnight_moves_expired_selection_to_newest_date`
   - **Purpose:** Prevent a date outside retention from remaining selected.
   - **Coverage:** Oldest retained date selected before midnight, then clock advances.
   - **Target effect:** Selection and report move to new local today.
7. `Empty_result_produces_normal_zero_state`
   - **Purpose:** Treat a successful empty result as normal, not an operational error.
   - **Coverage:** Successful zero-total query with no applications.
   - **Target effect:** Empty statistics are shown, error stays clear, and loading ends.
8. `Query_failure_produces_error_without_fabricated_empty_report`
   - **Purpose:** Prevent failed reads from appearing as zero usage.
   - **Coverage:** Query task failing with an I/O exception.
   - **Target effect:** Error is populated, statistics remain null, and loading ends.

#### Runtime wiring tests — active snapshot and retention after resume

1. `UsageCollectorTests.Statistics_snapshot_captures_clock_and_open_session_together`
   - **Purpose:** Keep the active session and report timestamp as one coherent observation.
   - **Coverage:** Foreground session with an injected clock advanced before snapshot capture.
   - **Target effect:** Snapshot carries the same `AsOfUtc` and an open session with no end time.
2. `UsageCollectorTests.Statistics_snapshot_has_no_active_session_before_foreground_or_after_boundary`
   - **Purpose:** Exclude absent and boundary-closed sessions from live totals.
   - **Coverage:** Snapshot before foreground, then after a lock boundary.
   - **Target effect:** Active session is null in both states and the closed session is persisted once.
3. `UsageCollectorTests.Resume_refreshes_the_current_window_and_starts_a_new_usage_session`
   - **Purpose:** Ensure resume restarts usage when the foreground window has not changed during sleep.
   - **Coverage:** Same foreground process before and after suspend, with explicit boundary and resume times.
   - **Target effect:** The pre-suspend session ends, a new one starts at resume, and the watcher is refreshed once.
4. `UsageCollectorTests.Resume_while_locked_does_not_count_the_lock_screen_and_unlock_refreshes_usage`
   - **Purpose:** Prevent the Windows lock screen from being counted as user application time after resume.
   - **Coverage:** Lock, lock-screen foreground event, suspend, resume while locked, restore app, unlock, and shutdown.
   - **Target effect:** No locked-screen interval is recorded; tracking resumes only after unlock refreshes the app.
5. `SessionPowerEventRouterTests.Resume_requests_resume_work_and_foreground_refresh_when_the_session_is_unlocked`
   - **Purpose:** Preserve both cleanup and foreground recovery actions after an ordinary resume.
   - **Coverage:** Suspend followed by resume while unlocked, with independent callback counts.
   - **Target effect:** Resume work and foreground refresh each run exactly once, without an extra session boundary.
6. `SessionPowerEventRouterTests.Resume_while_locked_requests_retention_without_refreshing_foreground`

   - **Purpose:** Run cleanup after sleep even when Windows resumes to a locked session.
   - **Coverage:** Locked → suspend → resume flow and callback counts.
   - **Target effect:** Retention is checked once while foreground refresh stays suppressed.

#### `UsageStatisticsWindowUiTests` and related Windows UI checks — real WPF behavior

1. `Statistics_button_opens_separate_statistics_window`
   - **Purpose:** Verify the feature is reachable without replacing the desktop pet.
   - **Coverage:** Real pet process, button invocation, separate statistics window, duplicate-open attempt.
   - **Target effect:** Exactly one visible statistics window opens and the pet remains visible.
2. `Statistics_window_shows_seeded_date_ranking_and_selected_title_detail`
   - **Purpose:** Verify database-to-view bindings and user selections using a real WPF window.
   - **Coverage:** Seeded prior date, total duration, app ranking, selected application and title durations.
   - **Target effect:** The displayed total is 2 h 15 min, apps rank correctly, and title details match the selected app.
3. `Statistics_window_shows_empty_state_for_a_date_without_usage`
   - **Purpose:** Ensure a normal no-activity date is not shown as an error.
   - **Coverage:** Unseeded retained date and empty/error automation states.
   - **Target effect:** Empty-state UI is visible and error UI is absent.
4. `Usage_tracking_startup_failure_does_not_prevent_statistics_window_from_opening`
   - **Purpose:** Keep the pet usable when optional tracking/database access fails.
   - **Coverage:** Real startup with an invalid database path, pet and statistics window, accessible error state.
   - **Target effect:** Both windows open, error text is readable, and no empty report is fabricated.
5. `Closing_and_reopening_statistics_window_reloads_current_data`
   - **Purpose:** Verify the statistics window can be closed and reused without stale data.
   - **Coverage:** Open, close via `WM_CLOSE`, mutate isolated SQLite data, reopen, and enumerate windows.
   - **Target effect:** One new window shows updated totals and no duplicate window remains.
6. `App_startup_prunes_expired_rows_and_clips_retained_boundary_rows`
   - **Purpose:** Prove retention is connected to actual application startup.
   - **Coverage:** Real launch with expired and cutoff-crossing rows pre-seeded in SQLite.
   - **Target effect:** Expired data is deleted, crossing data starts at cutoff, and the pet stays running.
7. `Usage_statistics_seed_uses_fixed_local_date_baseline`
   - **Purpose:** Prevent flaky UI seeds whose relative dates shift while a test runs.
   - **Coverage:** Explicit fixed local-date baseline and derived yesterday/boundary timestamp properties.
   - **Target effect:** Seed date calculations remain anchored to construction date.
8. `DesktopPuppyWindowUiTests.Desktop_puppy_window_exposes_a_stable_ui_automation_surface`
   - **Purpose:** Protect the real pet window's stable automation entry point used by the feature.
   - **Coverage:** Pet window Automation ID/name, close control identifier and invocation, and process exit.
   - **Target effect:** Automation can reliably locate and close the pet by stable identifiers.

### Post-merge verification — 2026-09-26

Run from `C:\Users\ton20\Documents\works\projects\desktop-puppy` on an interactive Windows desktop:

```powershell
dotnet build DesktopPuppy.sln --configuration Release --no-restore
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-build --no-restore
$env:DESKTOP_PUPPY_EXE = "C:\Users\ton20\Documents\works\projects\desktop-puppy\src\DesktopPuppy\bin\Release\net10.0-windows\DesktopPuppy.exe"
dotnet test tests/DesktopPuppy.UiTests/DesktopPuppy.UiTests.csproj --configuration Release --no-restore --filter "Category=Ui"
```

Observed on the merged `main` at `2aa0fd7`:

```text
Release build: 0 warnings, 0 errors
Unit tests: 101 passed, 0 failed
Windows UI tests: 11 passed, 0 failed
```

The UI test project must run on an interactive Windows desktop and requires `DESKTOP_PUPPY_EXE` to point to the built app. These tests verify windows, UI Automation, seeded reports, and startup retention; they do not verify actual operating-system lock/suspend notification delivery. The real 30-second timer refresh has since been verified separately by the long-running E2E recorded below; local-midnight rollover remains a separate manual check.

### Remaining verification and feature work

- Manually confirm on Windows that real lock/unlock and suspend/resume notifications stop and resume usage correctly. The current Windows power source reports generic suspend and does not claim to distinguish sleep from hibernate.
- Manually observe the live statistics window refresh over its 30-second interval and verify behavior across actual local midnight; automated tests cover injected-clock logic and activation refresh.
- Complete native monitor/work-area enumeration and connect DPI geometry policy to the production WPF placement path; verify mixed-DPI movement, monitor removal, and edge clamping on hardware or an equivalent Windows setup.
- Run a dedicated two-process check that a second launch activates the first pet.
- Remaining broader PRD work includes fullscreen detection, tray/startup registration, settings persistence/deletion, and AI/report features. The full PRD acceptance matrix is not complete.

## Real 30-second statistics refresh E2E — 2026-09-28

Added one real-process, real-WPF long-running UI test (`tests/DesktopPuppy.UiTests/UsageStatisticsRefreshE2ETests.cs`) whose purpose is to prove that an already-open statistics window refreshes new SQLite data through its own product timer, not a manual query, activation, reopen, or restart. A read-only `ReadListItemNames` snapshot helper was added to `tests/DesktopPuppy.UiTests/DesktopPuppyUiHarness.cs`; no product, schema, timer interval, default filter, or README change was made.

Verified on an interactive Windows desktop in the isolated worktree `C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy` (branch `codex/windows-e2e-acceptance`, baseline `6daab2a`):

```powershell
dotnet build src/DesktopPuppy/DesktopPuppy.csproj --configuration Release
$env:DESKTOP_PUPPY_EXE = (Resolve-Path 'src/DesktopPuppy/bin/Release/net10.0-windows/DesktopPuppy.exe').Path
dotnet build tests/DesktopPuppy.UiTests/DesktopPuppy.UiTests.csproj --configuration Release
dotnet test tests/DesktopPuppy.UiTests/DesktopPuppy.UiTests.csproj --configuration Release --no-build --filter "Category=LongRunningE2E"
dotnet test tests/DesktopPuppy.UiTests/DesktopPuppy.UiTests.csproj --configuration Release --no-build --filter "Category=Ui"
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release
```

Observed:

```text
Release app build: 0 warnings, 0 errors
Release UI test build: 0 warnings, 0 errors
Category=LongRunningE2E: 1 passed, 0 failed (33 s)
Category=Ui: 11 passed, 0 failed (21 s; excludes the long-running case)
Unit tests: 101 passed, 0 failed
```

`Statistics_window_refreshes_new_sqlite_data_on_its_real_timer` seeds `Yesterday` 09:00 local for 30 minutes of `BrowserApp`, waits for the displayed total `0 小时 30 分钟`, then appends a closed 15-minute `RefreshProbeApp` row on the same local date through a separate SQLite connection while the real window stays open. It then polls only Automation ID names read-only for at most 60 seconds, confirming `0 小时 45 分钟` and the `RefreshProbeApp` application row, with the pet process still running, the statistics window onscreen, and exactly one `UsageStatisticsWindow`. The 33-second run reflects the product's real 30-second `DispatcherTimer`; no focus, activation, reopen, or invocation occurs after the second insert.

This test verifies the timer-driven statistics refresh only. It does not verify operating-system lock/power notification delivery or monitor/DPI behavior, and the broader PRD acceptance matrix remains incomplete.


## Windows session/power acceptance harness — 2026-09-28

The opt-in visible acceptance controller documents two operator-driven Windows scenarios: actual lock/unlock and generic suspend/resume. It requires explicit opt-in, an absolute product executable path, and no already-running `DesktopPuppy` process. The controller never initiates OS transitions. Both manual scenarios remain **Not run**; no live Windows lock/unlock or suspend/resume notification delivery is claimed. The suspend source reports generic Suspend/Resume, not sleep-versus-hibernate classification.

### Fresh verification at Task 5

Commands were run from the isolated worktree `C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy` at base `1220f980da7981049f25bcb1297c3863b5e0b11e`:

```powershell
dotnet build DesktopPuppy.sln --configuration Release --no-restore
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore
```

- **Full unit suite:** 121 passed, 0 failed, 0 skipped. This includes Task 4's distinct pre-suspend/resumed-fixture regression.
- **Fresh Release solution/controller build:** the ordinary parallel/node-reuse attempts hit WPF markup-cache access denied; the cause was not established. A fresh serialized, no-incremental full-solution build then succeeded with 0 warnings and 0 errors using the command below. The full unit suite was run immediately afterward without rebuilding: 121 passed, 0 failed, 0 skipped.

```powershell
dotnet build DesktopPuppy.sln --configuration Release --no-restore --no-incremental --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-build --no-restore
```

The successful serialized build is the current verification result; the earlier access-denied attempts are retained as a note, not treated as proof of a specific root cause.
- **Latest safe UI smoke (2026-09-28, Task 4):** `DesktopPuppyWindowUiTests` 1/1 passed in a separate historical run. This smoke is not the full UI category.
- **Historical full `Category=Ui` result (2026-09-26):** 11/11 passed before the Task 4/5 changes. A post-acceptance full-category rerun is **Not run** because interaction tests use `SendInput` on the active desktop. The Task 4 report's failed prerequisite attempt is not a UI suite result.
- **Real 30-second refresh E2E (2026-09-28):** 1/1 passed in 33 seconds in a separate long-running category. This is not hardware power-event verification.
- Actual Windows lock/unlock: **Not run**. Actual Windows suspend/resume: **Not run**. Manual verification is still required.

The historical 11/11 UI suite, later 1/1 safe smoke, 1/1 long-running timer E2E, and fresh 121-test unit run are separate scopes/runs; none should be combined or described as a post-change full UI pass. Unit tests with injected event providers/validator inputs verify deterministic routing and boundaries only; they do not prove live OS event delivery.

The manual acceptance status vocabulary is distinct: **Not run** means no operator execution has been recorded; **Skipped** means prerequisites/opt-in prevented a run; **Incomplete** means a started run was cancelled or timed out; **Failed** means observed evidence violated the acceptance rules; and **Passed** requires actual ordered OS notifications, matching isolated persisted boundaries, and operator confirmation.

The 30-second statistics refresh has been exercised by the real long-running timer E2E described above. Verification across actual local midnight remains a distinct **Not run** manual check; injected-clock rollover tests do not replace it.

### Safety review

Reviewed the acceptance controller, event observer, owned-directory helper, and product database path selection. The harness has no API calls that initiate lock/unlock/sleep/wake, no credential APIs, and no synthetic/system-event injection path used to obtain an acceptance pass. Unit-level fake event tests remain simulations and are not reported as hardware acceptance. Temporary cleanup is guarded to the unique per-run child under the harness-owned temp root and refuses reparse-point replacement; if owned children cannot be stopped, cleanup retains the directory. The acceptance controller initializes and queries only its run-owned SQLite database passed explicitly to the isolated product process; it does not open the production database. No actual OS transition or input injection was performed for this documentation task.

## Native monitor/DPI placement and live acceptance — Task 7 (2026-09-29)

### Current branch and production connection

At Task 7 verification time, branch `codex/windows-e2e-acceptance` was at starting commit `7a22be97aa92fc69d1bb2128bc50e85c53c31640` in `C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy`. The only tracked Task 7 change updated this status document; the branch had not yet been merged then. After the final-review fixes were accepted, the branch was fast-forward-merged into `main` at `717bf593bf6d013bd34634a6e7efff87ebcc828f`; the merge was not pushed.

The native placement path is connected to the production pet window:

- `App` composes `Win32MonitorDisplayProvider`, `Win32WindowPositioner`, and `PetWindowPlacementService`. The service applies initial placement after the pet HWND is loaded and reconciles after location, per-monitor DPI, and display-settings changes.
- `PetWindow` marshals display-change reconciliation onto its WPF dispatcher. Initialization or later placement failures are caught and logged; placement initialization failure preserves ordinary WPF startup placement and does not prevent the offline pet from starting.
- Monitor enumeration, work-area/bounds queries, containing-monitor/DPI queries, and the window-positioner P/Invokes are under `src/DesktopPuppy/Native`. The pre-existing, unrelated `GetDoubleClickTime` P/Invoke remains in `PetWindow`; it is not part of monitor/DPI placement.
- The only native window mutation used by placement is `SetWindowPos` for the pet HWND, with `SWP_NOACTIVATE | SWP_NOZORDER`. A source search found no API that changes display mode or monitor topology (`ChangeDisplaySettings*`, `SetDisplayConfig`, or equivalent).
- `PetWindow.OnClosed` detaches its `DisplaySettingsChanged` handler and forgets its HWND placement state. `App.OnExit` disposes the placement service and then the monitor provider. The provider unsubscribes from `SystemEvents.DisplaySettingsChanged`, prevents new forwarding after disposal, and drains active callbacks; dispatch/shutdown races are guarded and caught.
- The mixed-DPI and monitor-removal acceptance controller uses read-only observations and does not initiate a display/topology change. Its cleanup is scoped to the isolated child and owned run directory; directory deletion depends on affirmative child-exit confirmation.

This is a source/lifecycle safety review, not evidence of live WPF behavior on a multi-monitor desktop.

### Fresh Task 7 verification

Verification date: **2026-09-29 (America/New_York, UTC-04:00)**. The serialized build reported 00:00:21.23 and the xUnit run reported 3 seconds. The execution transcript did not record separate command wall-clock start/end times; the final test-assembly artifact was written at 04:39:52.725 -04:00 during the build. The xUnit command followed immediately in the same invocation, so no unsupported exact xUnit clock time is asserted.

Commands ran serially from the Task 7 worktree using .NET SDK `10.0.401`:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build DesktopPuppy.sln --configuration Release --no-restore --no-incremental --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false
& "C:\Program Files\dotnet\dotnet.exe" test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-build --no-restore --verbosity minimal
```

Observed fresh results:

- **Serialized Release solution build:** exit code 0; build succeeded with **0 warnings and 0 errors**. Solution membership/build output included `DesktopPuppy`, `DesktopPuppy.WindowsAcceptance.Core`, `DesktopPuppy.WindowsAcceptance`, and `DesktopPuppy.Tests`. The acceptance controller was build-verified only; it was not launched.
- **Full deterministic `DesktopPuppy.Tests` suite:** exit code 0; **174 passed, 0 failed, 0 skipped**.
- **UI tests:** **Not run**. This includes `Category=Ui`, all UI smoke tests, and the long-running UI E2E. The UI harness can inject `SendInput` and launch a topmost pet window, so no UI test was run on the active desktop. The historical UI results elsewhere in this document are not Task 7 or post-acceptance-branch evidence.
- No test or build command launched the acceptance utility or performed a live display/topology action. No live monitor/DPI measurement was collected.

### Hardware acceptance and prerequisites

- **E2E-DISPLAY-01 — real mixed-DPI placement:** **Not run**.
- **E2E-DISPLAY-02 — real monitor removal and primary fallback:** **Not run**.
- The manual display utility was not launched or opted into. No preflight was run, so neither scenario is reported as `Skipped`; no hardware result is inferred from the build or fake-backed tests.
- The acceptance procedure requires Windows in a local interactive session (not Remote Desktop), at least two connected displays, both explicit acceptance opt-ins, an absolute path to the Release `DesktopPuppy.exe`, and no unrelated running `DesktopPuppy` process. Mixed-DPI acceptance additionally requires the same pet HWND to be observed on two displays with distinct measured DPI values. Monitor-removal acceptance requires Display 2 to be a secondary display, the primary to remain connected, and an operator to disconnect/disable and later restore the target display manually.
- These are documented prerequisites, **not observed environment facts for this run**: because the utility was not launched and live display enumeration was not performed, the actual display count, DPI values, topology, operator preflight, and removal outcome are unknown. Hardware acceptance remains **Not run**, not Passed or Skipped.
- Builds and deterministic unit tests—including fake-provider/native-boundary tests—do not establish real mixed-DPI movement, event delivery from Windows, or topology-removal recovery. Those checks remain for an operator on qualifying hardware.

### Scope and changed files

Task 7 changed `docs/implementation-status.md` as the only tracked file. The ignored ledger `progress.md` records the two safety rulings; no plan Markdown was changed. The requested execution report is at `C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\.superpowers\sdd\2026-09-28-monitor-dpi-placement-acceptance-plan\task-7-report.md`. No README, production code, or test code was changed.

## Final-review follow-up — 2026-09-29

The separate acceptance executable now has its own `app.manifest` wired through `ApplicationManifest`, declaring PerMonitorV2 awareness. The focused invalid-native-geometry privacy regression demonstrated the device-ID leak before the provider diagnostic was made identifier-free; it passes after the fix. Both `AcceptanceRunTests` facts now carry Purpose, Coverage, and Target effect annotations.

Fresh verification on the current fix: MSBuild evaluates `ApplicationManifest` to `app.manifest`, and the manifest parses as valid XML with `dpiAwareness=PerMonitorV2,PerMonitor` and `dpiAware=true/pm`. A serialized Release solution build succeeded with **0 warnings / 0 errors**; the full deterministic test suite passed **175/175**. The Task 7 section above remains the historical 174-test result from its own run.

`Category=Ui` was **Not run**. The acceptance utility was not launched. E2E-DISPLAY-01 mixed-DPI placement and E2E-DISPLAY-02 monitor removal/primary fallback both remain **Not run**; no display/topology change was made.

## Post-merge verification on main — 2026-09-29

The reviewed `codex/windows-e2e-acceptance` branch, including the final-review fixes, is now fast-forward-merged into `main` at `717bf593bf6d013bd34634a6e7efff87ebcc828f`. No push was performed. The Task 7 section above remains a historical record of its own verification (174/174 unit tests and a zero-warning build); it is not overwritten by the subsequent main-checkout run.

Post-merge verification in the main checkout:

- **Full deterministic unit suite:** 175 passed, 0 failed, 0 skipped.
- **Release solution build:** succeeded with 0 errors and 3 `NU1900` warnings. NuGet could not reach `https://api.nuget.org/v3/index.json` to retrieve vulnerability metadata; this environment/source warning is recorded rather than described as a warning-free build.
- **Windows UI tests (`Category=Ui`):** **Not run**. No UI harness that injects input was started on the active desktop.
- **Acceptance utilities:** not launched. **E2E-DISPLAY-01 mixed-DPI placement** and **E2E-DISPLAY-02 monitor removal/primary fallback** remain **Not run**. No display settings or monitor topology were changed, and no synthetic hardware pass was recorded.

## Built-in WinForms tray icon — 2026-09-30

### Selected approach and scope

The tray uses the built-in .NET Windows Forms `NotifyIcon` and `ContextMenuStrip`, hosted on the existing WPF Dispatcher. WPF remains the sole application and lifetime owner; no third-party tray package, WinForms top-level window/message loop, or direct Shell interop was added. The tray host owns its icon/menu resources and uses the embedded generic multi-resolution tray icon.

The Task 1–4 tray implementation spans these files:

- `src/DesktopPuppy/DesktopPuppy.csproj`
- `src/DesktopPuppy/App.xaml.cs`
- `src/DesktopPuppy/Pet/PetWindow.xaml.cs`
- `src/DesktopPuppy/Properties/AssemblyInfo.cs`
- `src/DesktopPuppy/Pet/Tray/Assets/DesktopPuppyTray.ico`
- `src/DesktopPuppy/Pet/Tray/ITrayApplicationActions.cs`
- `src/DesktopPuppy/Pet/Tray/ITrayIconHost.cs`
- `src/DesktopPuppy/Pet/Tray/ITrayPetWindow.cs`
- `src/DesktopPuppy/Pet/Tray/TrayApplicationActions.cs`
- `src/DesktopPuppy/Pet/Tray/TrayCommandService.cs`
- `src/DesktopPuppy/Pet/Tray/TrayContextMenuFactory.cs`
- `src/DesktopPuppy/Pet/Tray/TrayIconCommand.cs`
- `src/DesktopPuppy/Pet/Tray/TrayResourceCleanup.cs`
- `src/DesktopPuppy/Pet/Tray/WinFormsTrayIconHost.cs`
- `tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj`
- `tests/DesktopPuppy.Tests/Pet/Tray/TrayApplicationActionsTests.cs`
- `tests/DesktopPuppy.Tests/Pet/Tray/TrayCommandServiceTests.cs`
- `tests/DesktopPuppy.Tests/Pet/Tray/TrayContextMenuFactoryTests.cs`
- `tests/DesktopPuppy.Tests/Pet/Tray/TrayResourceCleanupTests.cs`

This Task 5 status update changes only this document. The implementation plan and source/test files were not changed in Task 5.

### Fresh automated verification

Run date: **2026-09-30**. Worktree: `C:\Users\ton20\.codex\worktrees\wpf-tray-icon\desktop-puppy`, branch `codex/wpf-tray-icon`. Build/test source identity: **`73dc5fb88c87163e24c68b531306cd448efc47c2`** (Task 4 reviewed and approved). SDK: **10.0.401**.

Serialized Release solution build command:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
& 'C:\Program Files\dotnet\dotnet.exe' build DesktopPuppy.sln --configuration Release --no-restore --no-incremental --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false -p:NuGetAudit=false
```

- **Exit code:** 0.
- **Result:** build succeeded; **0 errors, 26 warnings**. The warning summary comprised **24 NU1801** warnings for the configured nonexistent local NuGet source `C:\Program Files (x86)\Microsoft SDKs\NuGetPackages\` and **2 WFO0003** warnings about the existing high-DPI manifest configuration (including the generated WPF temporary project). These warnings did not fail the build and are recorded, not suppressed or described as zero-warning.
- **Solution projects in the output:** `DesktopPuppy`, `DesktopPuppy.WindowsAcceptance.Core`, `DesktopPuppy.WindowsAcceptance`, and `DesktopPuppy.Tests`.

Complete deterministic unit-suite command:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-build --no-restore --verbosity minimal -p:NuGetAudit=false --maxcpucount:1 --nodeReuse:false
```

- **Exit code:** 0.
- **Result:** **192 passed, 0 failed, 0 skipped; 192 total**. This is 17 more passing tests than the prior 175-test clean baseline; no regression was observed.
- `Category=Ui`, active-desktop automation, application launch, Explorer restart, and all manual tray acceptance were **not run**.

The build establishes compilation only. The deterministic suite covers automated unit behavior only; neither result establishes Explorer registration, visible shell placement, real-user menu interaction, two-process behavior, or taskbar recreation.

### Manual tray acceptance — operator required

#### TRAY-01 — Icon and menu

- **Purpose:** Verify real Explorer icon and menu availability/placement.
- **Coverage:** Run Release; inspect the normal/overflow tray; right-click; inspect labels, order, and placement; dismiss and reopen the menu.
- **Target effect:** One icon; correctly placed menu with exactly the approved commands; no duplicate icon.
- **Actual result:** Not executed; no Explorer icon or menu observation was collected. No result is inferred from source inspection, framework behavior, build, or unit tests.
- **Evidence/build identity:** No manual evidence. Automated evidence is limited to the Release build and 192 deterministic unit tests from source commit `73dc5fb88c87163e24c68b531306cd448efc47c2` (SDK 10.0.401); the app was not launched for acceptance.
- **Current status:** Not run — Manual required.

#### TRAY-02 — Hide, continue, restore, statistics

- **Purpose:** Prove Hide is not process shutdown.
- **Coverage:** Hide; verify the process remains; open and close statistics while hidden; restore.
- **Target effect:** Tracking/statistics continue while hidden and the same pet returns.
- **Actual result:** Not executed; process continuity, hidden-state statistics behavior, and restoration were not observed manually. No result is inferred from source inspection, framework behavior, build, or unit tests.
- **Evidence/build identity:** No manual evidence. Automated evidence is limited to the Release build and 192 deterministic unit tests from source commit `73dc5fb88c87163e24c68b531306cd448efc47c2` (SDK 10.0.401); the app was not launched for acceptance.
- **Current status:** Not run — Manual required.

#### TRAY-03 — Second launch and exit

- **Purpose:** Prove single-instance recovery and normal exit.
- **Coverage:** Hide; launch the Release app a second time; inspect process/window; exit from tray; separately check the existing close button.
- **Target effect:** The original pet restores; there is one product process/pet; exit removes the app/icon.
- **Actual result:** Not executed; second-process activation and both exit paths were not observed manually. No result is inferred from source inspection, framework behavior, build, or unit tests.
- **Evidence/build identity:** No manual evidence. Automated evidence is limited to the Release build and 192 deterministic unit tests from source commit `73dc5fb88c87163e24c68b531306cd448efc47c2` (SDK 10.0.401); no app launch or second process was used for acceptance.
- **Current status:** Not run — Manual required.

#### TRAY-04 — Explorer restart

- **Purpose:** Prove framework taskbar recreation behavior on the real machine.
- **Coverage:** The operator restarts Explorer normally while the app is running, waits for the taskbar, then checks the icon and menu. This check is never automated.
- **Target effect:** Exactly one icon returns and the menu remains usable without another pet/process.
- **Actual result:** Not executed; Explorer was not restarted and no post-restart icon/menu observation was collected. No result is inferred from source inspection, framework behavior, build, or unit tests.
- **Evidence/build identity:** No manual evidence. Automated evidence is limited to the Release build and 192 deterministic unit tests from source commit `73dc5fb88c87163e24c68b531306cd448efc47c2` (SDK 10.0.401); Explorer was not restarted.
- **Current status:** Not run — Manual required.

TRAY-01–04 remain pending operator execution; none is complete based on this automated verification.

## Final tray-review verification — 2026-09-30

The source/test fixes were committed first as **`9bc7f7ea3ce6dfed56b77f49ee5b1633ebfd562b`**, based directly on `9a3fb6d16bef3b69b8f7e5ca7df405c6969d69ed` in `C:\Users\ton20\.codex\worktrees\wpf-tray-icon\desktop-puppy`. Fresh checks below ran against that committed source identity using .NET SDK `10.0.401`.

### Verification commands and results

Focused tray test command:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
& 'C:\Program Files\dotnet\dotnet.exe' test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~DesktopPuppy.Tests.Pet.Tray' --verbosity minimal -p:NuGetAudit=false --maxcpucount:1 --nodeReuse:false
```

**Exit code 0.** One test file matched; **18 passed, 0 failed, 0 skipped, 18 total**.

Serialized Release solution build:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
& 'C:\Program Files\dotnet\dotnet.exe' build DesktopPuppy.sln --configuration Release --no-restore --no-incremental --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false -p:NuGetAudit=false
```

**Exit code 0.** The build succeeded with **0 errors and 26 warnings**: 24 `NU1801` warnings because the configured local NuGet source `C:\Program Files (x86)\Microsoft SDKs\NuGetPackages\` does not exist (12 against the app project and 12 against its generated WPF temporary project), and 2 `WFO0003` high-DPI manifest warnings (the app and generated WPF temporary project). The generated project in this run was `DesktopPuppy_s23ts5db_wpftmp.csproj`. These warnings are reported, not suppressed or described as zero-warning.

Full deterministic unit-test command, run after the solution build:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
& 'C:\Program Files\dotnet\dotnet.exe' test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-build --no-restore --verbosity minimal -p:NuGetAudit=false --maxcpucount:1 --nodeReuse:false
```

**Exit code 0.** **193 passed, 0 failed, 0 skipped; 193 total** (3 seconds).

### Scope and manual status

`Category=Ui`, all UI tests, app launch, Explorer automation/restart, and manual TRAY acceptance were **not run**. **TRAY-01, TRAY-02, TRAY-03, and TRAY-04 remain `Not run — Manual required`.** In particular, the second-instance unit test now asserts only that two activations route to the injected `ITrayApplicationActions` while tray startup has failed and the host has only one start attempt. It does not prove WPF window identity; **TRAY-03 remains the real manual check**. The build and deterministic tests establish neither Shell icon visibility nor Explorer behavior.
## Tray merge verification on main — 2026-09-30

The reviewed `codex/wpf-tray-icon` branch was fast-forward-merged into `main` at `b1cfbda330c99267871baf92a42d3b484c4d5fe1`; it was not pushed. The existing latest status content from the primary checkout was retained as the base for this update; this section adds the tray implementation record rather than replacing historical or later status notes.

Fresh verification ran from the primary `main` worktree after the merge using .NET SDK `10.0.401`:

- **Serialized Release solution build:** succeeded, 0 errors, 5 warnings: 3 `NU1900` vulnerability-data-source warnings because `https://api.nuget.org/v3/index.json` was unreachable, and 2 `WFO0003` high-DPI manifest warnings. No warnings were suppressed.
- **Full deterministic unit suite:** 193 passed, 0 failed, 0 skipped.
- **UI/active-desktop tests:** not run. The application was not launched for acceptance; Explorer was not restarted.
- **TRAY-01–04:** remain **Not run — Manual required**, as detailed above. No real Explorer icon/menu behavior, hidden-window statistics, second-process restore/exit, or taskbar recreation is inferred from this build or unit suite.

## Statistics correctness fixes and next acceptance stage — 2026-10-01

Source identity: uncommitted working-tree changes on `main` based on `ec1c241`. Existing unrelated README/design/plan edits were preserved; no commit or push was made.

- Added foreground sampling to the existing background advance, before title promotion; no-observation refreshes do not promote a pending title. Lock/suspend pause state is unchanged by polling.
- Added an immediate startup foreground sample.
- Added throttled 30-second last-confirmed title checkpoints and retryable non-fatal timer error handling. Failed closes retain their original observed boundary separately, so reopening the same title does not absorb lock/suspend downtime.
- Added transactional legacy checkpoint-column migration and explicit single-instance-owner startup recovery before retention/collection. Observers can still initialize the store without sealing the owner's live titles.
- Preserved retention-clipped title starts during subsequent checkpoint/close upserts.
- Updated the UI retention assertion to check seeded IDs rather than incorrectly assume no real foreground sessions are collected at startup.

Fresh verification:

- **Release solution build:** exit 0, 0 errors, 5 warnings (3 cached NU1900 vulnerability-source warnings, 2 WFO0003 DPI-manifest warnings).
- **Complete deterministic unit suite:** 212 passed, 0 failed, 0 skipped (19 additional cases versus 193).
- **Separate UI-project build:** exit 0, 0 errors, 1 NU1900 warning.
- **Selected desktop UI suite:** 9 passed, 0 failed, 0 skipped in 23 seconds (final rerun), filtered to `DesktopPuppyWindowUiTests`, `UsageStatisticsWindowUiTests`, and `Category=LongRunningE2E`. Test databases were isolated; no mouse/keyboard injection or system transition occurred. This is not the full `Category=Ui` suite.
- The initial selected UI run had one outdated total-row-count assertion failure; after correcting that test to target the seed IDs, all nine selected cases passed.

Limits: checkpoints recover confirmed title detail only. An abrupt termination can still lose the unclosed foreground application session; do not claim at-most-30-second loss for total application usage. Scheduling, unavailable observations, and failed writes can increase title loss. Synchronous startup/shutdown SQLite remains a separate technical-debt item.

Evidence and commands: `docs/verification/2026-10-01-statistics-correctness.md`.
Next ordered operator steps: `docs/verification/windows-acceptance-checklist.md`. **TRAY-01–04, actual lock/unlock/suspend/resume, E2E-DISPLAY-01–02, and complete input-injecting UI regression remain Not run.** The next stage requires the operator's real Shell/OS/hardware actions; neither unit nor selected UI results substitutes for them.

## Animation runtime integration — 2026-10-07

- Integrated the saved Snoopy frames as packaged WPF resources; source and review files remain intact. One shared crop (250,80,800,1120) and display size preserve alignment.
- Runtime actions: idle, blink, happy, greeting, click-smug, hit-startle/recoil/recover, held drag-lift, landing, special-tongue/glance/smile and classic anticipate/tongue/settle. New interaction interrupts playback; completed sequences return to idle. Corrected drag release after the native DragMove modal loop.
- Startup greeting; idle rotation every seven seconds; single click shows smug, double click hit, right click rotates four specials, drag holds lifted pose then lands.
- Fresh verification: 214/214 deterministic tests; 12/12 Category=Ui tests, including startup greeting, all four special peaks, hit recovery and idle return, plus existing drag/bubble/statistics checks. Release solution build succeeded with 0 errors / 3 cached NU1900 warnings on incremental build (earlier app rebuild also emitted WFO0003).
- Initial sandbox test runs aborted due to testhost connection timeout; the successful runs used the interactive Windows environment. Manual hardware/Shell acceptance remains pending. Changes are included in the current local animation integration commit; no push was requested.

## Right-click AI vertical slice — 2026-10-08

Implemented in the managed `right-click-ai` worktree from source base `726d3da`. Main and its existing untracked directories remain unchanged; no merge or push was performed.

- Added a WPF AI settings window, reachable from the pet AI button and tray. AI/right-click switches persist independently of provider-specific DPAPI current-user encrypted key blobs. Stored keys are not read back into the UI. Blank or corrupt/unreadable keys produce fixed notifications; no raw exceptions or provider bodies are logged.
- Added independent AI SQLite tables, transactional idempotent initialization and 15-local-day retention using the existing cutoff policy. Tests prove coexistence with preexisting usage sessions and preservation of configuration/key blobs.
- DeepSeek uses JSON mode and `deepseek-flash`, six candidates per batch, eight-second attempt timeout and up to five serial attempts. Accepted candidates accumulate until at least four; retries receive existing candidates and a variation number. Invalid full text is not stored. Jev uses official TypeSafe `/v1/systemone`, `jev-latest`, one Choice and five-second attempt timeouts. Jev retry does not regenerate DeepSeek candidates. Both use 1/2/4/8-second backoff; HTTP response bodies are capped at 64 KiB and redirects disabled.
- Local validation checks 10–80 Unicode text elements, plain single-paragraph content, banned stock phrases, punctuation/emoji limits, six explicit styles, exact/near duplication and consecutive style repetition. Duplicate cutoff is NFKC-normalized letter/digit bigram Dice >= 0.75. These heuristics cannot prove semantic factual truth; prompts explicitly constrain scene grounding and treat titles as data. Live language-quality acceptance remains required.
- Added a single in-flight right-click coordinator and a monotonic 15-second bubble deadline. Animations always run locally. Extra in-flight clicks do not queue. Expired/closed/replaced/hidden/fullscreen results are recorded without display; only actually displayed text enters repetition history. Disabled or unconfigured AI preserves existing speech. Notifications also obey ownership/deadline. Final API failure has no local speech fallback; exit cancels pending calls.
- The foreground context observes the existing native watcher and refreshes, preserving the last external app/title when clicking the pet; no second hook or expanded data collection was added. Context is frozen per request and capped at 128 app-name / 512 title UTF-16 code units.
- Fullscreen detection checks Shell exclusive Direct3D state plus visible, non-minimized, non-cloaked, unframed monitor-sized windows every 500 ms. Normal framed/maximized windows and desktop/shell/pet windows are excluded. Automatic fullscreen hiding and explicit user hiding are separate; statistics and in-flight requests continue. Results completed while suppressed do not replay on restoration.

### Automated evidence and review

- Clean serialized Release solution build: exit 0, 0 errors, 2 existing WFO0003 DPI-manifest warnings. Worktree restore used the NuGet cache and explicit nuget.org source with NuGet audit disabled; no fresh vulnerability-scan claim is made.
- Complete deterministic suite: **260 passed, 0 failed, 0 skipped**. Coverage includes real eight-second cancellation/retry, partial candidate accumulation, malformed probabilities, actual Windows DPAPI round trip/corruption, old usage-table coexistence, retention, late completion and shutdown cancellation.
- Independent read-only review found two P2 lifecycle defects: preflight removal of existing speech and late failure notices. Four new regression cases were observed failing before the fixes; the full suite passed afterward.
- Desktop UI uses private database fixtures, including previously unisolated harness cases, so it cannot reuse real API keys or the ordinary user database. One full run passed 14/14. A later run had one startup discovery timeout (13 passed); the exact case passed alone afterward. The timeout cause is not established and is not described as fixed. Final full-run result is recorded below.
- **Final complete `Category=Ui` rerun:** exit 0, **14 passed, 0 failed, 0 skipped**, 25 seconds, against the final Release executable after lifecycle/copy changes. The isolated case passed 1/1 in a separate run; it is not added to the full-suite count. The earlier startup timeout remains a recorded limitation.
- Real online DeepSeek/Jev calls: **Passed on 2026-10-08** using user-configured DPAPI keys and an isolated WPF fixture. Production routed right-click event, coordinator, validator, providers and real bubble rendering were exercised: 6 accepted candidates, one generation attempt, one selection attempt, displayed within 10.60 seconds, one request after two in-flight clicks, and one matching display-history row. Physical mouse input and the complete production App startup/tray path were not exercised by this fixture. Evidence: `docs/verification/2026-10-08-live-ai.md`. Real borderless/exclusive/multi-monitor fullscreen, tray/Explorer, lock/power and monitor/DPI hardware acceptance remain **Not run**. Unit geometry checks and UI smoke are not hardware acceptance.

Current next action: real fullscreen and remaining Windows hardware/Shell acceptance. Both keys have been saved through AI settings and isolated live API/bubble verification has passed. The initial thinking-enabled baseline was 7.062 / 6.520 / 6.019 seconds. Explicitly disabling DeepSeek thinking reduced three fixture click-to-bubble samples to 1.654 / 1.908 / 2.226 seconds, median 1.908 seconds, with six accepted candidates and one generation/selection attempt each. The production request now explicitly disables thinking; its HTTP contract regression was observed failing before the change and the full 260-test suite passed afterward. The user confirmed the running production pet's manual right-click test passed on 2026-10-08. This does not imply stable two-second latency or real fullscreen acceptance. Evidence and test-fixture limitations are in `docs/verification/2026-10-08-ai-stage-timing.md`. Preserve the original manual Windows acceptance backlog. Follow-up features are AI greetings/activities, reports, full settings, and startup registration.

### User acceptance and local integration — 2026-10-08

- User reported “测试通过” after testing the production right-click pet with thinking explicitly disabled. Mark basic manual right-click acceptance **Passed — user reported**; detailed close/replace/fullscreen/hardware scenarios remain separate.
- Fast-forward merged the reviewed right-click AI implementation (`c8adaec`) and non-thinking/acceptance update (`06ea7a5`) into local `main` from `726d3da`, as requested by the user. No push was performed. Existing untracked verification/runtime directories and the user database were preserved. The managed worktree remains available; normal execution uses the main checkout's Release executable.
- Fresh merged-tree Release solution build: exit 0, **0 errors / 2 existing WFO0003 DPI warnings**. Separate UI-project build: exit 0, **0 errors / 0 warnings**.
- Fresh complete deterministic tests on main: **260 passed, 0 failed, 0 skipped**. Complete `Category=Ui` regression against main's Release executable: **14 passed, 0 failed, 0 skipped**, 24 seconds, with isolated fixture databases.
- The first UI invocation lacked `DESKTOP_PUPPY_EXE` and failed fixture preconditions (13 failed / 1 passed); it did not establish an application regression. The complete rerun with the main executable explicitly configured passed as above. No real Shell/fullscreen/lock/power/DPI hardware acceptance is inferred from these tests.

## Left/right edge docking integration — 2026-10-08

- User approved the 45-degree sprites before implementation and accepted the running feature with “可以，合并吧”. The changes were already in the primary main checkout, so local integration records them directly as a commit on main; no synthetic branch merge or push is needed.
- Added half-head left/right docking with a 24 DIP threshold, 250ms reveal/collapse and 800ms pointer-leave delay; bubbles protect expansion. Captured half-head dragging preserves the grab anchor, including DPI-aware geometry and capture-loss cleanup. Working-area clipping prevents hidden body spill into adjacent monitors.
- Full pre-integration evidence: Release solution build 0 errors / 2 existing WFO0003 warnings, separate UI build 0 errors / 0 warnings, 269 deterministic tests and 16 complete Category=Ui desktop tests passed. Commit-time deterministic rerun passed 269/269. UI regression was not repeated for documentation-only integration, and the restarted user pet remained running.
- Evidence: `docs/verification/2026-10-08-edge-docking.md`. User acceptance does not independently establish mixed-DPI, display-removal, side-taskbar or complete Windows Shell/OS acceptance.
