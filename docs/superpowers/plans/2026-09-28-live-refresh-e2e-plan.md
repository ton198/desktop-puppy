# Real 30-Second Statistics Refresh E2E Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove that the real running WPF statistics window reads a new SQLite usage session through its own 30-second timer, without a manual refresh, window reactivation, or restart.

**Architecture:** Add one separately tagged long-running UI E2E case to the existing UI test project. Reuse the current process-launch, isolated SQLite seed, and UI Automation harness; add no product test hooks. Seed a retained prior local date, observe the baseline, append a closed session from a second SQLite connection, and only read UI Automation state until the refreshed result appears.

**Tech Stack:** .NET 10, WPF, xUnit 2.9.2, Microsoft.Data.Sqlite 10, Windows UI Automation.

**Spec:** `docs/superpowers/specs/2026-09-28-windows-e2e-acceptance-design.md` sections 3.1, 4, 5 E2E-STAT-01, 7, and 8.

## Global Constraints

- The test uses its own temporary SQLite database and a separate real Desktop Puppy process; it never opens or deletes the user's production database.
- The selected date is yesterday in the local time zone, within the existing 15-day retention window; every seeded and appended session is closed and belongs to that date.
- After appending the second row, do not invoke controls, activate/focus the statistics window, reopen it, restart the app, call `RefreshAsync`, or simulate a timer tick.
- UI Automation after insertion is read-only and polls for at most 60 seconds; it does not bring either WPF window to the foreground.
- The new test has `Category=LongRunningE2E`; the existing `Category=Ui` filter must continue excluding it.
- Keep each added test's **Purpose**, **Coverage**, and **Target effect** in its source comments.
- Preserve all pre-existing user changes; only edit files listed in a task and never stage unrelated paths.

---

## File Map

- `tests/DesktopPuppy.UiTests/UsageStatisticsRefreshE2ETests.cs` — new real-timer long-running test, isolated seed setup, UI assertions, and per-case Purpose/Coverage/Target effect.
- `tests/DesktopPuppy.UiTests/UsageStatisticsSeed.cs` — existing `Yesterday`, `YesterdayStartUtc`, `AppendUsage`, and `DatabasePath` are reused unchanged; `AppendUsage` opens a separate SQLite store/connection.
- `tests/DesktopPuppy.UiTests/DesktopPuppyUiHarness.cs` — reuse process/window/selection helpers; add a read-only `ReadListItemNames(AutomationElement root, string listAutomationId)` snapshot method so the E2E poll captures the last total and app rows without activation.
- `docs/implementation-status.md` — after implementation, record the exact filtered command, observed result and the fact that it waits for the real timer; do not claim lock/power or monitor validation from this test.

## E2E-STAT-01 — Running data refreshes on the real 30-second timer
- **Purpose:** Prove an already-open real WPF statistics window refreshes through its product-owned 30-second `DispatcherTimer`, not a manual query, window activation, reopen, restart, or ViewModel test.
- **Coverage:** Release app process; isolated SQLite; fixed retained prior local date; baseline closed session; a second-connection insert while the real window remains open; read-only UIA polling.
- **Target effect:** Within 60 seconds after insertion, the total changes from 30 to 45 minutes and the new application row appears, while the pet and statistics windows remain open.

## Task 1: Add the Real Timer Refresh Tes

**Files:**
- Create: `tests/DesktopPuppy.UiTests/UsageStatisticsRefreshE2ETests.cs
- Modify: `tests/DesktopPuppy.UiTests/DesktopPuppyUiHarness.cs
- Reuse: `tests/DesktopPuppy.UiTests/UsageStatisticsSeed.cs` without changes

**Interfaces:**
- Consume `UsageStatisticsSeed.DatabasePath`, `.Yesterday`, `.YesterdayStartUtc`, and `.AppendUsage(string processName, string title, DateTimeOffset start, TimeSpan duration)`.
- Consume `DesktopPuppyUiHarness.Start(executablePath, usageDatabasePath)`, existing UI Automation lookup/selection/wait methods, and `IsProcessRunning`.
- Produce a `[Fact]` tagged `[Trait("Category", "LongRunningE2E")]` named `Statistics_window_refreshes_new_sqlite_data_on_its_real_timer`.

- [ ] **Step 1: Add the test and read-only list snapshot helper, with its purpose, coverage, and target effect.** The helper locates the list by Automation ID, reads currently available list item text, and never calls focus/activate/invoke. It returns the current snapshot immediately rather than waiting for a count, so timeout diagnostics can report partial or stale UI state. Use this sequence:
  1. Create `UsageStatisticsSeed`; append one `BrowserApp` interval on `Yesterday` from local 09:00 for 30 minutes. In the new test file define `LocalInstant(DateOnly date, TimeOnly time)` using `DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified)` followed by `TimeZoneInfo.ConvertTimeToUtc(..., TimeZoneInfo.Local)`; do not use `YesterdayStartUtc.AddHours(...)`, which can shift the wall-clock time across a daylight-saving boundary.
  2. Start the Release `DesktopPuppy.exe` with that seed database. Find the pet, open one statistics window, select `Yesterday`, and wait until the displayed total is exactly `0 小时 30 分钟` and the application list contains only `BrowserApp`.
  3. Append one closed `RefreshProbeApp` interval on the same prior local date from local 11:00 for 15 minutes through `UsageStatisticsSeed.AppendUsage`, using the same `LocalInstant(seed.Yesterday, new TimeOnly(11, 0))` helper and separate SQLite store/connection.
  4. From this point forward poll only `FindByAutomationId(...).Current.Name` and the new read-only `ReadListItemNames(...)`; wait at most 60 seconds for `0 小时 45 分钟` and both app rows. Assert `RefreshProbeApp` is present, the pet process is still running and visible, `statisticsWindow.Current.IsOffscreen` is false, and `harness.CountTopLevelWindows("UsageStatisticsWindow")` is exactly one. These are read-only HWND/UIA checks and must not call `Activate`, focus, or invoke. On timeout include the last total name, last app rows, elapsed wait, process state, statistics-window visibility/count, and DB path in the thrown assertion.


- [ ] **Step 2: Build the Release app and UI test project before running the slow case.** Run:

```powershell
dotnet build src/DesktopPuppy/DesktopPuppy.csproj --configuration Release
$env:DESKTOP_PUPPY_EXE = (Resolve-Path 'src/DesktopPuppy/bin/Release/net10.0-windows/DesktopPuppy.exe').Path
dotnet build tests/DesktopPuppy.UiTests/DesktopPuppy.UiTests.csproj --configuration Release


Expected: both build successfully with no warnings or errors attributable to the change; the environment variable points to the just-built Release executable.

- [ ] **Step 3: Run only the new long case on the interactive desktop.** Run:

```powershell
$env:DESKTOP_PUPPY_EXE = (Resolve-Path 'src/DesktopPuppy/bin/Release/net10.0-windows/DesktopPuppy.exe').Path
dotnet test tests/DesktopPuppy.UiTests/DesktopPuppy.UiTests.csproj --configuration Release --no-build --filter "Category=LongRunningE2E"


Expected: the test passes only after the real 30-second timer reloads the new row. Confirm the test's elapsed time and that no foreground action occurs after the insertion.

- [ ] **Step 4: Prove the fast filter remains fast and excludes the new test.** Run:

```powershell
dotnet test tests/DesktopPuppy.UiTests/DesktopPuppy.UiTests.csproj --configuration Release --no-build --filter "Category=Ui"


Expected: existing UI smoke tests pass, `Statistics_window_refreshes_new_sqlite_data_on_its_real_timer` is not listed as executed, and the command does not wait 30 seconds for this new case.

- [ ] **Step 5: Run the unit suite and record verified results.** Run:

```powershell
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release


Expected: the existing unit suite passes. Then update only the E2E/verification portion of `docs/implementation-status.md` with actual command outputs, not predicted counts.

- [ ] **Step 6: Review the focused diff.** Confirm no product code, database schema, timer interval, default UI filter, README content, or unrelated user file changed. Stage/commit only the new test and status update if repository integration is explicitly chosen later.

## Completion Checklis

- [ ] Real UI test waits on the already-running app's natural 30-second interval.
- [ ] No activation-triggered refresh path is used after database insertion.
- [ ] UI result includes exact 45-minute aggregate and the new application row.
- [ ] Long-running and ordinary UI categories run independently.
- [ ] Unit, UI smoke, long-running results and remaining manual/hardware checks are reported separately.
