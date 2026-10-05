# Usage Statistics Window and 15-Day Retention Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Track each step with checkboxes and review each task before continuing.

**Goal:** Add a separate window for viewing daily foreground-application and window-title usage, include the active session in live totals, and enforce 15 local calendar days of retained usage data.

**Architecture:** Reuse the existing SQLite store, retention cutoff policy, and local-day aggregator. Add a date-aware runtime retention coordinator, a synchronized active-session snapshot and a statistics query service; keep WPF presentation and asynchronous refresh in focused statistics-window components. Wire the window into the pet without making usage collection a prerequisite for pet startup.

**Tech Stack:** .NET 10, C# with nullable reference types, WPF, xUnit 2.9, Microsoft.Data.Sqlite 10, existing Windows UI Automation test harness.

**Spec:** `docs/superpowers/specs/2026-09-25-usage-statistics-window-and-retention-design.md`

## Global Constraints

- Keep the current local date and the previous 14 local dates; derive the cutoff from the oldest retained local midnight, converted to UTC.
- Persist intervals in UTC and use the current local time zone only to select dates and split intervals at actual local-day boundaries.
- Count an open session or title interval only through one captured `asOfUtc` instant per refresh.
- Refresh on open, activation, and date selection, then every 30 seconds while the statistics window is open.
- Run SQLite reads and aggregation away from the WPF dispatcher; do not let an older query replace the latest date selection.
- Keep usage tracking optional; distinguish empty data from read failure and do not terminate the pet when statistics are unavailable.
- Do not add charts, export, cloud sync, or AI summaries.
- Preserve the user's existing unstaged changes in `README.md`, the earlier plan/spec documents, and `.dotnet-home/`; only stage files explicitly listed for a task.

---

## File Map

- `src/DesktopPuppy/Usage/UsageRetentionCoordinator.cs` — run retention on startup and once per local date, using the existing cutoff policy.
- `tests/DesktopPuppy.Tests/Usage/UsageRetentionCoordinatorTests.cs` — deterministic tests for startup, date rollover, resume/delayed checks, idempotency, and failure retry.
- `src/DesktopPuppy/Usage/UsageCollector.cs` — capture the active session and UTC `asOf` atomically under the existing collector gate.
- `src/DesktopPuppy/Usage/UsageStatisticsQueryService.cs` — merge/cap live and stored intervals and project one selected date into immutable statistics models.
- `tests/DesktopPuppy.Tests/Usage/UsageStatisticsQueryServiceTests.cs` — query aggregation, active-session race, date selection, clipping, empty and failure behavior.
- `tests/DesktopPuppy.Tests/Usage/UsageCollectorTests.cs` — verify active collector snapshots and boundary behavior.
- `src/DesktopPuppy/Pet/Statistics/UsageStatisticsViewModel.cs` — date choices, selected application, loading/error/empty states, and stale-request suppression.
- `tests/DesktopPuppy.Tests/Pet/UsageStatisticsViewModelTests.cs` — deterministic presentation-state and asynchronous request tests.
- `src/DesktopPuppy/Pet/Statistics/UsageStatisticsWindow.xaml` and `.xaml.cs` — separate WPF window, date selector, total, application ranking, title detail, and 30-second refresh lifecycle.
- `src/DesktopPuppy/Pet/PetWindow.xaml` and `.xaml.cs` — accessible Statistics button and request event; preserve the small pet layout and existing interaction behavior.
- `src/DesktopPuppy/App.xaml.cs` — compose the query service and window, handle optional startup failure, and own the retention check timer/window lifecycle.
- `tests/DesktopPuppy.UiTests/DesktopPuppyUiHarness.cs` — pass a per-test database path and locate the statistics window by Automation ID.
- `tests/DesktopPuppy.UiTests/UsageStatisticsSeed.cs` — seed isolated past-date usage records for UI tests.
- `tests/DesktopPuppy.UiTests/UsageStatisticsWindowUiTests.cs` — verify real WPF entry, date selection, seeded ranking/title detail, empty/error state, and reopening behavior.
- `docs/implementation-status.md` — record delivered feature and verified test results after implementation.

## Task 1: Connect 15-Day Retention to Runtime

**Interfaces:** Add `UsageRetentionCoordinator(Action<DateTimeOffset> pruneBefore, TimeZoneInfo timeZone)` with `bool CheckAndPrune(DateTimeOffset nowUtc)`. It derives the local date from `nowUtc`, calls `UsageRetentionPolicy.GetCutoffUtc(nowUtc, timeZone)` on the first check and after each local-date change, and marks that date successful only after pruning returns. A thrown prune error leaves the date retryable. `App` invokes it after `UsageSqliteStore.Initialize()`, checks it on a one-minute timer, and checks it again after resume; the timer performs no database work on same-date checks.

**Files:**
- Create: `src/DesktopPuppy/Usage/UsageRetentionCoordinator.cs`
- Create: `tests/DesktopPuppy.Tests/Usage/UsageRetentionCoordinatorTests.cs`
- Modify: `src/DesktopPuppy/App.xaml.cs`

**Per-test purpose, coverage, and target effect:**

1. `First_check_prunes_using_oldest_retained_local_midnight`
   - **Purpose:** Ensure runtime retention starts immediately rather than waiting for a day rollover.
   - **Coverage:** First call, injected local time zone, and cutoff from `UsageRetentionPolicy`.
   - **Target effect:** The callback receives exactly the UTC instant for the start of the oldest of the 15 retained local dates.
2. `Repeated_checks_on_same_local_date_do_not_prune_again`
   - **Purpose:** Prevent unnecessary database cleanup on repeated timer checks.
   - **Coverage:** Multiple check times within one local date.
   - **Target effect:** Exactly one prune occurs for that date.
3. `First_check_after_local_date_change_prunes_new_cutoff_once`
   - **Purpose:** Keep retention rolling while the app stays open.
   - **Coverage:** Check immediately before and after local midnight, then repeat after midnight.
   - **Target effect:** One additional prune uses the new date's cutoff; later same-date checks do not prune again.
4. `Delayed_check_after_resume_catches_up_to_current_local_date`
   - **Purpose:** Ensure sleeping through one or more dates does not preserve stale history.
   - **Coverage:** A check whose timestamp is several local dates after the prior successful check.
   - **Target effect:** One prune runs using the current date, not a sequence of obsolete intermediate cutoffs.
5. `Failed_prune_does_not_mark_date_complete_and_can_be_retried`
   - **Purpose:** Avoid silently skipping cleanup after a transient database error.
   - **Coverage:** Callback throws on the first attempt and succeeds on a later check of the same date.
   - **Target effect:** The failed date remains due and the successful retry records it as complete.

- [ ] **Step 1: Write test 1 and observe RED.** Use a recording callback and a fixed zone/time; assert the exact cutoff equals `UsageRetentionPolicy.GetCutoffUtc(nowUtc, zone)`. Run:

```powershell
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --filter "FullyQualifiedName~First_check_prunes_using_oldest_retained_local_midnight"
```

Expected: fail because `UsageRetentionCoordinator` does not exist.

- [ ] **Step 2: Add tests 2–5 one at a time.** For each test, run its focused filter before implementation and confirm the missing coordinator behavior is the reason for failure. Use a DST-observing zone for the rollover cutoff assertion; do not substitute a fixed 360-hour duration.
- [ ] **Step 3: Implement the minimal coordinator.** Keep the callback and time zone injected; update the last-successful date only after the callback returns. Use a private lock so the resume check and timer check cannot prune the same date concurrently. Core implementation shape:

```csharp
public bool CheckAndPrune(DateTimeOffset nowUtc)
{
    var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, _timeZone).DateTime);
    lock (_gate)
    {
        if (_lastSuccessfulLocalDate == localDate)
        {
            return false;
        }

        _pruneBefore(UsageRetentionPolicy.GetCutoffUtc(nowUtc, _timeZone));
        _lastSuccessfulLocalDate = localDate;
        return true;
    }
}
```

- [ ] **Step 4: Run all retention coordinator and existing SQLite retention tests.** Confirm all five new behaviors and the existing cutoff/clipping/DST tests pass.
- [ ] **Step 5: Wire runtime checks in `App.xaml.cs`.** Run the initial check immediately after `Initialize()`. Catch and report cleanup failures without abandoning collector startup. Add a one-minute timer that calls `CheckAndPrune(clock.UtcNow)` and dispose it in both startup rollback and `OnExit`. Compose the existing resume callback so it refreshes the foreground watcher and calls `CheckAndPrune` immediately; on each same-date timer check the coordinator must return without touching SQLite.
- [ ] **Step 6: Build and run unit tests; inspect the diff and commit only the coordinator, tests, and `App.xaml.cs`.** Use commit message `feat: run fifteen-day usage retention`.

## Task 2: Expose Live Session Snapshot and Build Statistics Query

**Interfaces:** Add `UsageCollector.CaptureStatisticsSnapshot()` returning `UsageCollectorStatisticsSnapshot(DateTimeOffset AsOfUtc, ForegroundSession? ActiveSession)`. Capture both values while holding the collector gate. Add immutable query types:

```csharp
public sealed record WindowTitleStatistics(string WindowTitle, TimeSpan Duration);
public sealed record ApplicationStatistics(
    string ProcessName,
    TimeSpan Duration,
    IReadOnlyList<WindowTitleStatistics> WindowTitles);
public sealed record DailyUsageStatistics(
    DateOnly LocalDate,
    TimeSpan TotalDuration,
    IReadOnlyList<ApplicationStatistics> Applications);

public interface IUsageStatisticsQuery
{
    Task<DailyUsageStatistics> QueryAsync(DateOnly localDate, CancellationToken cancellationToken = default);
}
```

`UsageStatisticsQueryService` implements the interface using `UsageSqliteStore`, `UsageCollector`, and an injected `TimeZoneInfo`. It snapshots the collector first, then reads SQLite. Merge sessions by ID, prefer a persisted closed row when the same ID exists, cap every session/title end later than `AsOfUtc` to `AsOfUtc`, and turn the active session into an interval ending at `AsOfUtc`. Then call `DailyUsageAggregator`, filter the requested local date, group title durations under their process, sort applications and titles by descending duration, and set the daily total to the sum of application durations. Empty results return zero with no application rows. Execute the synchronous SQLite/aggregation work inside `Task.Run` with cancellation support; do not access WPF from this service.

**Per-test purpose, coverage, and target effect:**

1. `Statistics_snapshot_captures_clock_and_open_session_together`
   - **Purpose:** Ensure live statistics have one coherent timestamp and active session.
   - **Coverage:** Running collector, foreground event, injected fixed clock.
   - **Target effect:** Snapshot `AsOfUtc` equals the fixed clock and `ActiveSession` identifies the open process.
2. `Statistics_snapshot_has_no_active_session_before_foreground_or_after_boundary`
   - **Purpose:** Avoid counting absent, locked, suspended, or already-closed foreground sessions as active.
   - **Coverage:** No initial foreground, then session boundary closure.
   - **Target effect:** Snapshot is null when there is no open session; the closed session remains persisted once.
3. `Query_counts_active_session_only_through_snapshot_as_of`
   - **Purpose:** Make current application time visible without waiting for the foreground session to close.
   - **Coverage:** Active in-memory session, fixed `AsOfUtc`, selected local date.
   - **Target effect:** Application duration equals `AsOfUtc - StartedAt`, with no future time.
4. `Query_deduplicates_active_session_when_closed_row_appears_during_read`
   - **Purpose:** Prevent duplicate time at the active-to-persisted transition.
   - **Coverage:** Active snapshot and stored closed row share one session ID; stored end is later than `AsOfUtc`.
   - **Target effect:** The process is counted once and its interval ends at `AsOfUtc`.
5. `Query_clips_title_interval_closed_after_snapshot_as_of`
   - **Purpose:** Keep an interval closed during the database read from extending beyond the refresh instant.
   - **Coverage:** Stored title interval ends after `AsOfUtc`.
   - **Target effect:** The title duration ends exactly at `AsOfUtc`.
6. `Query_returns_selected_local_date_with_ranked_apps_and_title_details`
   - **Purpose:** Produce the screen's complete daily hierarchy from existing event data.
   - **Coverage:** Multiple apps, repeated sessions, repeated titles, and records on adjacent local dates.
   - **Target effect:** Only the selected date is returned; app/title durations are grouped and sorted descending; total equals the sum of app rows.
7. `Query_returns_empty_daily_statistics_when_date_has_no_usage`
   - **Purpose:** Represent a valid no-activity date without treating it as a read failure.
   - **Coverage:** Empty store result for a retained date.
   - **Target effect:** Correct date, zero total, empty application list, no exception.
8. `Query_propagates_store_read_failure_for_the_view_to_report`
   - **Purpose:** Let the presentation show an unavailable state rather than false zero usage.
   - **Coverage:** Disposed or failing store.
   - **Target effect:** Query task faults with the storage exception; the view model, not this service, converts it to user-facing error state.

- [ ] **Step 1: Add the snapshot test to `UsageCollectorTests.cs` and observe RED.** Start a collector with the existing fake watcher, emit a foreground snapshot, set the fake clock, call `CaptureStatisticsSnapshot()`, and assert both timestamp and process name. Run the focused filter and confirm the method is missing.
- [ ] **Step 2: Add snapshot boundary test and run RED.** Assert no active session before the first foreground event and after `OnSessionBoundary`; assert the sink receives the closed session only once.
- [ ] **Step 3: Implement `UsageCollectorStatisticsSnapshot` and `CaptureStatisticsSnapshot()`.** Read `_clock.UtcNow` and `_sessionTracker.OpenSession` under `_gate`; return an immutable record and do not persist or mutate the session.
- [ ] **Step 4: Create `UsageStatisticsQueryServiceTests.cs` and add query tests 3–8 individually.** Use temporary SQLite files for persisted rows and the existing fake watcher/clock for active rows. For test 4, seed a persisted row with the active snapshot's ID and an end later than its captured `AsOfUtc`; for test 5, seed a title end later than `AsOfUtc`. Verify each test fails for the intended missing query behavior before implementing.
- [ ] **Step 5: Add query/result types and implement the query service.** The snapshot must be obtained before SQLite reads. Normalize future ends for both session and title intervals before calling the aggregator. Preserve the aggregator's actual-local-midnight and DST behavior; do not duplicate aggregation logic in the UI.
- [ ] **Step 6: Run `UsageCollectorTests`, `UsageStatisticsQueryServiceTests`, `DailyUsageAggregatorTests`, and SQLite store tests; then run all unit tests.** Commit only the collector, query service, and their tests with message `feat: query live daily usage statistics`.

## Task 3: Add Date-Aware Presentation State and Statistics Window

**Interfaces:** `UsageStatisticsViewModel` consumes `IUsageStatisticsQuery`, `TimeZoneInfo`, and an injected `Func<DateTimeOffset>`. It exposes exactly 15 local dates, selects today initially, and has `SelectedDate`, `SelectedApplication`, `Statistics`, `IsLoading`, `ErrorMessage`, `LoadAsync()`, `SelectDateAsync(DateOnly)`, `SelectApplication(string?)`, and `RefreshAsync()`. Each request receives a monotonically increasing ID; only the newest request may update state. Clear the prior error on a new request; set an error only for a failed query; an empty result remains a normal zero/empty state.

**Per-test purpose, coverage, and target effect:**

1. `View_model_exposes_today_and_previous_fourteen_local_dates`
   - **Purpose:** Match the date selector exactly to the retention window.
   - **Coverage:** Fixed UTC clock near a local-date boundary and injected time zone.
   - **Target effect:** Fifteen consecutive local dates appear, newest first, with today selected.
2. `Selecting_date_queries_and_displays_that_date`
   - **Purpose:** Ensure the user can navigate retained history.
   - **Coverage:** Select a prior date and a fake query response for that date.
   - **Target effect:** `SelectedDate` and `Statistics.LocalDate` match the selected date.
3. `Selecting_application_exposes_only_its_title_details`
   - **Purpose:** Keep window-title details tied to the chosen application.
   - **Coverage:** Two application summaries with distinct title lists.
   - **Target effect:** Selected application's title rows are shown; changing selection changes details without changing daily totals.
4. `Older_query_completion_cannot_replace_newer_date_selection`
   - **Purpose:** Prevent stale background reads from repainting the wrong day.
   - **Coverage:** Two controllable asynchronous fake responses completed in reverse order.
   - **Target effect:** The final model state belongs to the most recently selected date.
5. `Empty_result_and_query_failure_produce_distinct_states`
   - **Purpose:** Avoid conflating no activity with unavailable statistics.
   - **Coverage:** Successful zero/empty response and a faulted query.
   - **Target effect:** Empty response has no error; failure sets `ErrorMessage` and does not set an empty report as if it were valid data.

- [ ] **Step 1: Add view-model tests 1–2 and confirm RED.** Use a fixed clock and a fake `IUsageStatisticsQuery` that records requested `DateOnly` values. Run focused filters and confirm the view-model type is missing.
- [ ] **Step 2: Add tests 3–5 and confirm RED individually.** For test 4, use two `TaskCompletionSource<DailyUsageStatistics>` responses and complete the older request last. For test 5, separately assert a successful empty response and a thrown query error.
- [ ] **Step 3: Implement `UsageStatisticsViewModel`.** Create dates by converting the injected UTC clock to the injected time zone and subtracting 0–14 local dates. Guard each completed request with its captured request ID and selected date. Avoid adding a UI framework or MVVM package.
- [ ] **Step 4: Implement `UsageStatisticsWindow.xaml` with stable UI Automation IDs.** Provide a date `ComboBox`, total duration text, application list, selected-application title list, and distinct empty/loading/error text. Use bindings to the view model; format durations as hours/minutes without changing the underlying `TimeSpan` values.
- [ ] **Step 5: Implement window refresh lifecycle in code-behind.** Load on `Loaded`, refresh on `Activated` and date selection, and use a `DispatcherTimer` with a 30-second interval while open. Stop the timer and invalidate in-flight results on `Closed`. Keep the statistics window non-topmost and independent of the pet's fixed size.
- [ ] **Step 6: Run the view-model tests and build the WPF application in Release.** Verify no dispatcher-bound properties are written from the background query task. Commit only the new statistics presentation files and their tests with message `feat: add daily usage statistics window`.

## Task 4: Wire Pet Entry, Optional Startup, and Windows UI Coverage

**Files:**
- Modify: `src/DesktopPuppy/Pet/PetWindow.xaml`
- Modify: `src/DesktopPuppy/Pet/PetWindow.xaml.cs`
- Modify: `src/DesktopPuppy/App.xaml.cs`
- Modify: `tests/DesktopPuppy.UiTests/DesktopPuppyUiHarness.cs`
- Modify: `tests/DesktopPuppy.UiTests/DesktopPuppyWindowUiTests.cs`
- Create: `tests/DesktopPuppy.UiTests/UsageStatisticsSeed.cs`
- Create: `tests/DesktopPuppy.UiTests/UsageStatisticsWindowUiTests.cs`

**Per-test purpose, coverage, and target effect:**

1. `Statistics_button_opens_separate_statistics_window`
   - **Purpose:** Verify the visible feature is reachable without enlarging or replacing the pet.
   - **Coverage:** Real application process, pet Automation ID, Statistics button invocation, separate window Automation ID.
   - **Target effect:** Both windows are visible in the same process; the pet remains available and unchanged in behavior.
2. `Statistics_window_shows_seeded_date_ranking_and_selected_title_detail`
   - **Purpose:** Verify that WPF bindings present real stored data and date/application selections reach the query.
   - **Coverage:** Isolated SQLite database seeded with two prior-date applications and title intervals; date ComboBox; app selection; title detail list.
   - **Target effect:** The seeded date, total, ranking order, and selected application's title/duration are visible and correct.
3. `Statistics_window_shows_empty_state_for_a_date_without_usage`
   - **Purpose:** Verify a normal no-activity day is not presented as an error.
   - **Coverage:** Select an unseeded retained date.
   - **Target effect:** Empty-state text is visible and error-state text is absent.
4. `Usage_tracking_startup_failure_does_not_prevent_statistics_window_from_opening`
   - **Purpose:** Preserve pet startup when optional tracking/database access fails.
   - **Coverage:** Launch with a database path that cannot be opened, then invoke Statistics.
   - **Target effect:** Pet and statistics windows open; the statistics window explains that data is unavailable.
5. `Closing_and_reopening_statistics_window_reloads_current_data`
   - **Purpose:** Ensure the window lifecycle is reusable and begins a fresh query after reopening.
   - **Coverage:** Open, close, update the seeded database, and reopen from the pet.
   - **Target effect:** The reopened window displays the updated seeded result; no second stale window remains.
6. `App_startup_prunes_expired_rows_and_clips_retained_boundary_rows`
   - **Purpose:** Prove the application actually invokes retention after SQLite initialization, not just that the coordinator and store work independently.
   - **Coverage:** Seed one expired session and one session crossing the computed local-date cutoff, launch the app, then inspect the same isolated database.
   - **Target effect:** The expired row is gone, the crossing row remains with its start clipped to the exact cutoff, and the pet remains running.

- [ ] **Step 1: Add a Statistics button to the pet window.** Give it `AutomationProperties.AutomationId="StatisticsButton"`, an accessible name and tooltip in Chinese, and a click event that raises `StatisticsRequested`. Keep existing close control, drag/click handling, and pet dimensions intact.
- [ ] **Step 2: Compose statistics services and one reusable window in `App.xaml.cs`.** After usage tracking succeeds, create `UsageStatisticsQueryService`; on button request, activate the existing visible statistics window or create/show a new one. Clear the stored window reference on `Closed`. If usage setup failed, still open a statistics window with an unavailable state. Closing the statistics window must not trigger application shutdown.
- [ ] **Step 3: Add an isolated UI-test database override.** In `App.xaml.cs`, pass `Environment.GetEnvironmentVariable("DESKTOP_PUPPY_USAGE_DB_PATH")` into `UsageSqliteStore`; null/blank must preserve the existing LocalApplicationData default. Extend the harness to set a child-process environment variable. Seed yesterday's records so test-run foreground activity today cannot change expected historical values.
- [ ] **Step 4: Write and run UI test 1 before completing its wiring.** Use the existing `DesktopPuppyUiHarness`, invoke `StatisticsButton`, wait for the statistics window by Automation ID, and assert the pet remains visible. Confirm RED because the button/window does not yet exist, then complete Task 1–3 integration needed for GREEN.
- [ ] **Step 5: Add `UsageStatisticsSeed.cs` and test 2.** Create a unique temporary database; initialize the store; insert two closed sessions and matching closed title intervals on yesterday's local date; start the executable with that database path; select yesterday; assert total, descending application order, and selected-title detail through UI Automation.
- [ ] **Step 6: Add UI tests 3–6 individually.** For each, run its focused filter and observe the intended failure before fixing it. Test 4 must use a path that is an existing directory to make SQLite initialization fail deterministically. Test 5 updates the isolated database after closing the window and verifies reopening displays the new record.
- [ ] **Step 7: Run the complete Windows UI suite and all unit tests.** Check that closing stats stops its timer, opening it again starts a fresh refresh, startup failures remain non-fatal, and all automation elements have stable IDs. Commit only pet/App integration and UI harness/tests with message `feat: connect usage statistics to the pet`.

## Task 5: Update Status and Verify the Complete Feature

**Files:**
- Modify: `docs/implementation-status.md`

- [ ] **Step 1: Update implementation status from verified results.** Record the 15-date runtime cleanup, statistics entry/window, active-session inclusion, list each new test class and its purpose/coverage/target effect, and distinguish automated coverage from manual verification of real Windows foreground/session/power delivery. Do not mark live OS validation complete unless performed.
- [ ] **Step 2: Run final Release verification.** Run the complete unit project, full solution build, and Windows UI project against the built executable. Commands:

```powershell
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release
dotnet build DesktopPuppy.sln --configuration Release
$env:DESKTOP_PUPPY_EXE = (Resolve-Path 'src/DesktopPuppy/bin/Release/net10.0-windows/DesktopPuppy.exe').Path
dotnet test tests/DesktopPuppy.UiTests/DesktopPuppy.UiTests.csproj --configuration Release --filter 'Category=Ui'
git diff --check
```

Expected: all unit and Windows UI tests pass, Release build has no warnings/errors, and `git diff --check` reports no whitespace errors. Record actual counts, not expected counts, in the status file.

- [ ] **Step 3: Review `git diff --stat` and `git status --short`.** Confirm only planned feature files and `docs/implementation-status.md` changed; leave the user's pre-existing modifications untouched. Commit only the status file with message `docs: record usage statistics feature status`.

## Manual Windows Verification After Automated Tests

- Lock/unlock the Windows session and verify no usage accrues while locked.
- Suspend/resume Windows and verify the active app session closes/reopens at the correct boundaries and retention catches up after resume.
- Leave the statistics window open for at least 30 seconds and verify the current application's displayed duration advances on refresh.
- Switch across local midnight and verify yesterday/today are split correctly and the 15-date selector rolls forward.



