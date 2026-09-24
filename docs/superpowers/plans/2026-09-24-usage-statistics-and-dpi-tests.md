# Usage Statistics and DPI Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. This execution is inline in the current session.

**Goal:** Implement the 34 approved unit-test cases for power/session routing, local-day usage aggregation, 15-local-day retention, and DPI-aware geometry, together with only the testable core behavior required to pass them.

**Architecture:** Keep normalized session-event routing, daily aggregation, retention cutoff calculation, and DPI math in deterministic core classes. Keep Win32 notification registration, monitor enumeration, and actual WPF DPI behavior outside unit tests and report them as Windows integration work. Preserve existing tracker, collector, SQLite serialization, and placement contracts.

**Tech Stack:** .NET 10, C# nullable enabled, xUnit 2.9, Microsoft.Data.Sqlite 10, WPF Windows target.

**Spec:** `docs/superpowers/specs/2026-09-24-usage-statistics-and-dpi-testability-design.md`

## Global Constraints

- Keep all interval instants persisted in UTC; use an injected `TimeZoneInfo` only for local-day and retention calculations.
- Treat intervals as half-open `[start, end)`; cap open title intervals at explicit `asOfUtc`.
- Retain the current local date plus the previous 14 local dates; delete wholly expired intervals and clip intervals overlapping cutoff.
- Keep native Win32 and WPF behavior out of unit tests; do not add production dependencies just for tests.
- Do not edit the main checkout's pre-existing uncommitted changes.
- Write each behavior test before its production implementation, run the focused test to observe the intended failure, implement minimally, and rerun focused plus full unit tests.

---

## File Map

- `src/DesktopPuppy/Usage/SessionPowerEventRouter.cs` — normalized event enum and routing to the existing `ForegroundSessionTracker` boundary callback; no native window or P/Invoke.
- `tests/DesktopPuppy.Tests/Usage/SessionPowerEventRouterTests.cs` — cases A-01 through A-08.
- `src/DesktopPuppy/Usage/DailyUsageAggregator.cs` — immutable daily application/title result types and a pure aggregator over sessions/title intervals.
- `tests/DesktopPuppy.Tests/Usage/DailyUsageAggregatorTests.cs` — cases B-01 through B-09.
- `src/DesktopPuppy/Usage/UsageRetentionPolicy.cs` — 15-local-date cutoff calculation.
- `src/DesktopPuppy/Usage/UsageSqliteStore.cs` — usage-table prune/delete/clip operation.
- `tests/DesktopPuppy.Tests/Usage/UsageSqliteStoreRetentionTests.cs` — cases C-01 through C-10 using isolated temporary SQLite files.
- `src/DesktopPuppy/Pet/Placement/DpiGeometryPolicy.cs` — pure double-precision scale conversion.
- `tests/DesktopPuppy.Tests/Pet/DpiGeometryPolicyTests.cs` — cases D-01 through D-04 and D-06 through D-07.
- `tests/DesktopPuppy.Tests/Pet/PetPlacementPolicyTests.cs` — add D-05 without duplicating existing fallback/default clamp tests.
- `docs/implementation-status.md` — record new unit coverage and keep native integration gaps explicit.

## Task 1: Route Normalized Session/Power Events

**Interfaces:** `SessionPowerEvent` has `Locked`, `Unlocked`, `Suspend`, `Hibernate`, `Resume`, and `Shutdown` values. `SessionPowerEventRouter` receives an `Action<SessionBoundary, DateTimeOffset>` and exposes `OnEvent(SessionPowerEvent event, DateTimeOffset at)`. It maps `Locked -> Lock`, `Suspend -> Sleep`, `Hibernate -> Hibernate`, `Shutdown -> Shutdown`; unlock/resume/unknown values do not synthesize foreground activity. Production composition passes `ForegroundSessionTracker.OnSessionBoundary` as the callback.

**Test:** `tests/DesktopPuppy.Tests/Usage/SessionPowerEventRouterTests.cs`

- [x] **Step 1: Write A-01 and observe RED.** Instantiate a router with a list-capturing callback; send `Locked` at a fixed timestamp; assert the sole callback is `(SessionBoundary.Lock, timestamp)`. Run: `dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --no-restore --configuration Release --filter "FullyQualifiedName~Locked_event_routes_lock_boundary_at_event_time"`. Expected: fails because the router contract does not yet exist.
- [x] **Step 2: Implement the minimum router contract.** Add the enum/class and only the `Locked` mapping required by A-01. Do not add Win32 registration.
- [x] **Step 3: Run A-01 GREEN, then add A-02 through A-08 individually.** Use the exact inputs and outcomes in spec cases A-02..A-08: suspend maps to Sleep; hibernate remains distinct; shutdown maps to Shutdown; unlock does not reopen; resume waits for an explicit foreground snapshot; unknown values are ignored; duplicate close notifications do not duplicate the closed-session callback.
- [x] **Step 4: For each added case, run its focused filter and observe the expected failure before changing mapping/state behavior; then rerun the case to PASS.** Use test-name fragments from the spec. Run all router tests afterward: `dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --no-restore --configuration Release --filter "FullyQualifiedName~SessionPowerEventRouterTests"`.
- [x] **Step 5: Run all unit tests and commit.** Run the full unit-test project. Commit only router source and its test file with message `feat: add testable session event routing`.

## Task 2: Aggregate Usage by Local Day

**Interfaces:** `DailyUsageAggregator.Aggregate(sessions, titleIntervals, asOfUtc, timeZone)` returns app buckets keyed by local `DateOnly` and process, plus title buckets keyed by local date, process, and title. Closed session duration uses `EndedAt`; a null title end uses `asOfUtc`. Split each UTC interval at actual local-midnight instants and sum elapsed UTC `TimeSpan`, not wall-clock hour labels.

**Test:** `tests/DesktopPuppy.Tests/Usage/DailyUsageAggregatorTests.cs`

- [x] **Step 1: Write B-01 and observe RED.** Construct one session from 09:00 to 10:30 UTC; aggregate with `TimeZoneInfo.Utc`; assert one process/date bucket equals 90 minutes. Focused filter: `FullyQualifiedName~Same_day_session_contributes_full_duration_to_one_local_day`. Expected: missing aggregator/result contract.
- [x] **Step 2: Implement the minimal same-day aggregation.** Add immutable result records and sum closed session intervals without database or clock access.
- [x] **Step 3: Add and cycle B-02 through B-05.** Test 23:30-00:30 split (30/30), exact-midnight end (no zero bucket), negative-offset local-date selection, and summing two same-process sessions without merging a second process. For each case, run its focused filter and verify the pre-fix failure, then implement only the missing behavior.
- [x] **Step 4: Add and cycle B-06 through B-07.** Split title detail by local date/title; cap an open interval at explicit `asOfUtc`. Assert repeated calls with identical inputs are identical and no future duration appears.
- [x] **Step 5: Add and cycle B-08 through B-09.** Use the `Eastern Standard Time` zone and exact UTC instants from the spec. Assert 60 minutes on 2026-03-08 across the spring gap and 120 minutes on 2026-11-01 across the repeated fall hour.
- [x] **Step 6: Run the complete aggregator class, then all unit tests.** Ensure the sum of split buckets equals elapsed UTC input duration and no empty bucket is returned. Commit source/tests as `feat: add local-day usage aggregation`.

## Task 3: Enforce Fifteen Local Dates of SQLite History

**Interfaces:** `UsageRetentionPolicy.GetCutoffUtc(nowUtc, timeZone)` converts `nowUtc` to a local date, subtracts 14 calendar dates, resolves that date's local midnight, and returns the UTC instant. `UsageSqliteStore.PruneBefore(cutoffUtc)` deletes closed rows with `EndedAt <= cutoffUtc`, clips starts of overlapping sessions/intervals to cutoff, preserves null end for active title intervals, and touches only the two telemetry tables.

**Test:** `tests/DesktopPuppy.Tests/Usage/UsageSqliteStoreRetentionTests.cs`

- [x] **Step 1: Write C-01 and observe RED.** Fix `nowUtc` and a time zone; assert cutoff is midnight for the oldest of 15 local dates, converted to UTC. Run the focused filter. Expected: missing policy method.
- [x] **Step 2: Implement cutoff policy and run C-01 GREEN.** Do not subtract a fixed 360 hours.
- [x] **Step 3: Add and cycle C-02 through C-06 against a temporary SQLite database.** Seed exact-cutoff/expired/retained sessions and closed/open title intervals. Assert wholly expired rows are deleted, start-at-cutoff rows remain, crossing rows keep identity/details and are clipped, and an open title remains open. Verify every test against actual store reads, not mocked SQL calls.
- [x] **Step 4: Add and cycle C-07 through C-10.** Run cleanup twice and compare reads; add a sentinel unrelated table and preserve it; calculate across the spring DST date using local-calendar arithmetic; clip with a non-UTC local zone and assert read-back instants are unchanged UTC instants.
- [x] **Step 5: Run all retention and existing store tests, then all unit tests.** Commit source/tests as `feat: retain fifteen local days of usage history`.

## Task 4: Add Pure DPI Geometry Conversion Coverage

**Interfaces:** `DpiGeometryPolicy` exposes double-precision conversion between DIPs and device pixels with an explicit positive scale. It performs no OS calls and does not round. Pass converted geometry and converted minimum-visible margin in consistent units to `PetPlacementPolicy`.

**Tests:** `tests/DesktopPuppy.Tests/Pet/DpiGeometryPolicyTests.cs`, plus `PetPlacementPolicyTests.cs` for D-05.

- [x] **Step 1: Write D-01 and observe RED.** Assert conversion at scale 1.0 is identity. Run its focused filter; expected: missing pure policy.
- [x] **Step 2: Implement identity conversion and run D-01 GREEN.**
- [x] **Step 3: Add and cycle D-02 through D-04.** Verify 80 DIPs × 1.25 = 100 device pixels, 300 device pixels ÷ 1.5 = 200 DIPs, and round trips at 1.25/1.5/2.0 differ by no more than `1e-9` DIPs. Run focused filters before and after each conversion change.
- [x] **Step 4: Add D-05 to placement tests.** Select a saved secondary monitor with negative origin, clamp in consistent units, and assert the expected negative-coordinate result while preserving the visible-edge invariant.
- [x] **Step 5: Add and cycle D-06 and D-07.** Convert a 20-device-pixel margin to DIPs at 1.0 and 1.5 scales, clamp on all edges, and assert the physical visible amount is 20 pixels within `1e-9`; test that 20 DIPs at 1.5 is not mistaken for 20 physical pixels.
- [x] **Step 6: Run both placement test classes and the full unit suite.** Commit source/tests as `feat: add pure dpi geometry policy`.

## Task 5: Document Coverage and Verify Integration Boundaries

**Files:** `docs/implementation-status.md`.

- [x] **Step 1: Update the unit-test inventory.** Record new test count, categories, and which behaviors remain outside unit scope; explicitly retain real session/power message delivery and real mixed-DPI WPF movement under Windows integration/manual verification.
- [x] **Step 2: Run final verification.** Run the complete unit project in Release, build `DesktopPuppy.sln` in Release with `--no-restore`, run the separate Windows UI suite if the interactive desktop is available, and run `git diff --check`.
- [x] **Step 3: Review repository diff and commit the status update.** Confirm no files in the main checkout changed. Commit documentation as `docs: record usage and dpi test coverage`.

## Commands

Run from the isolated worktree with the configured .NET 10 SDK:

```powershell
$env:DOTNET_CLI_HOME = 'C:\Users\ton20\Documents\works\projects\desktop-puppy\.dotnet-home'
& 'C:\Program Files\dotnet\dotnet.exe' test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --no-restore --configuration Release
& 'C:\Program Files\dotnet\dotnet.exe' build DesktopPuppy.sln --no-restore --configuration Release
$env:DESKTOP_PUPPY_EXE = 'C:\Users\ton20\.codex\worktrees\foreground-usage-slice\desktop-puppy\src\DesktopPuppy\bin\Release\net10.0-windows\DesktopPuppy.exe'
& 'C:\Program Files\dotnet\dotnet.exe' test tests/DesktopPuppy.UiTests/DesktopPuppy.UiTests.csproj --no-restore --configuration Release --filter 'Category=Ui'
git diff --check
```
