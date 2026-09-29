# Desktop Puppy Implementation Status

> This file is the handoff point for future conversations. Read it before changing code.

**Last updated:** 2026-09-26
**Latest verified step:** Merged the end-to-end daily usage statistics and 15-local-date retention feature to `main` (`2aa0fd7`); post-merge verification passed 101 unit tests, 11 Windows UI tests, and a Release build with 0 warnings / 0 errors.
**Current phase:** Phase 4 in progress — foreground collection, session/power wiring, local-day aggregation, 15-date retention, and the statistics window are implemented; live OS acceptance checks and native monitor/DPI integration remain.
**Current branch:** `main`
**Worktree:** `C:\Users\ton20\Documents\works\projects\desktop-puppy`
**Latest integration commit:** `2aa0fd7` (`Merge branch 'codex/usage-statistics-window-retention'`)
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
