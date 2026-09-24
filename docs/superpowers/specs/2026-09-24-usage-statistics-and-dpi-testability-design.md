# Usage Statistics and DPI Testability Design

## Goal

Add focused, deterministic unit tests for the next foreground-usage and monitor-placement behaviors. Where production behavior is missing, introduce only the small core policy needed to make the behavior testable. Win32 message delivery, native monitor discovery, and real WPF DPI behavior remain Windows integration concerns.

## Current State and Existing Coverage

The `codex/foreground-usage-slice` branch already tests foreground-session opening/switching, Desktop Puppy exclusion, title debounce, collector composition, SQLite round-trips, and basic monitor placement. In particular, `ForegroundSessionTrackerTests.Session_boundaries_close_the_open_session` covers the tracker API for `Lock`, `Sleep`, `Hibernate`, and `Shutdown`; `A_foreground_event_after_unlock_starts_a_new_session` covers the tracker's post-boundary behavior; and `PetPlacementPolicyTests` covers a removed-monitor fallback and a 20-pixel visible edge on the primary monitor.

Those existing tests do not verify how operating-system notifications map into tracker boundaries, daily rollups, retention cleanup, or DPI conversion. The new tests below target those gaps and must not duplicate the existing policy tests without adding a new behavior.

## Contracts Under Test

- A normalized session/power event is routed to a `SessionBoundary` with its event timestamp. Unlock/resume never fabricates a foreground-window event.
- Usage intervals are half-open `[start, end)`. A closed session uses its `EndedAt`; an open title interval is capped at an explicit `asOfUtc` input. No test or policy reads the machine clock.
- Local-day statistics use an injected `TimeZoneInfo`. Each interval is split at local date boundaries, and durations are summed by local date and process; title intervals additionally retain title-level detail.
- The retained window is the current local date plus the previous 14 local dates. Its cutoff is local midnight at the start of the oldest retained date. A row wholly before the cutoff is deleted; a row overlapping it is clipped to the cutoff. The cutoff is inclusive for interval starts.
- DPI conversion is a pure calculation with an explicit scale. Geometry must be normalized into one unit before placement/clamping; 20 physical pixels of visible pet edge is the invariant. Real monitor/DPI discovery is not part of the unit-test contract.

## Test Inventory

Each proposed test states its purpose, the inputs/behavior it covers, and the expected result. Test names are illustrative but should remain descriptive and behavior-focused.

### A. Session/power event routing

#### A-01 — `Locked_event_routes_lock_boundary_at_event_time`
- **Purpose:** Prove that the lock notification closes usage at the event time instead of at a later polling or shutdown time.
- **Coverage:** Route a normalized `Locked` event at `T1` while a session opened at `T0` is active; capture the boundary kind and timestamp delivered to the tracker.
- **Target effect:** Exactly one `Lock` boundary is delivered at `T1`, and the closed session's `EndedAt` is `T1`.

#### A-02 — `Suspend_event_routes_sleep_boundary`
- **Purpose:** Protect the mapping between an ordinary suspend notification and the existing sleep boundary.
- **Coverage:** Send a normalized `Suspend` event through the production router using a recording boundary receiver.
- **Target effect:** The receiver gets exactly `SessionBoundary.Sleep` with the original event timestamp; no other boundary is emitted.

#### A-03 — `Hibernate_event_routes_hibernate_boundary`
- **Purpose:** Ensure a hibernate notification is not collapsed into the ordinary sleep classification.
- **Coverage:** Send a normalized `Hibernate` event through the same routing seam.
- **Target effect:** The receiver gets exactly `SessionBoundary.Hibernate` with the original timestamp.

#### A-04 — `Shutdown_event_routes_shutdown_boundary`
- **Purpose:** Ensure an orderly shutdown closes the active usage interval through the existing shutdown boundary.
- **Coverage:** Route a normalized shutdown event while a session is active.
- **Target effect:** The receiver gets exactly `SessionBoundary.Shutdown` at the event time and the session is closed once.

#### A-05 — `Unlock_does_not_synthesize_a_foreground_session`
- **Purpose:** Prevent unlock from attributing time to whichever application happens to be foreground without a new foreground observation.
- **Coverage:** Open a session, route `Locked`, then route `Unlocked` without sending a foreground-window snapshot.
- **Target effect:** The old session is closed at lock time; after unlock the tracker still has no open session and no new session is persisted.

#### A-06 — `Resume_waits_for_next_foreground_event_before_reopening_usage`
- **Purpose:** Verify that resuming the computer does not invent an app identity or start time.
- **Coverage:** Route `Suspend` and `Resume`, then separately deliver a foreground snapshot at `T2`.
- **Target effect:** No session is open between `Resume` and `T2`; the new session starts at `T2` and belongs to the snapshot's process.

#### A-07 — `Unknown_session_event_is_ignored`
- **Purpose:** Make forward-compatible event handling safe when the native adapter sees an unsupported notification.
- **Coverage:** Route an unknown normalized event while a session is active.
- **Target effect:** No boundary is delivered and the active session remains unchanged.

#### A-08 — `Repeated_boundary_without_open_session_does_not_emit_duplicate_close`
- **Purpose:** Make repeated lock/suspend notifications harmless when the first event has already closed the session.
- **Coverage:** Route the same closing event twice with no intervening foreground event.
- **Target effect:** The boundary router may receive both OS notifications, but the tracker emits only one closed session and remains closed; no exception occurs.

### B. Local-day usage aggregation

#### B-01 — `Same_day_session_contributes_full_duration_to_one_local_day`
- **Purpose:** Establish the basic rollup result for a session that does not cross a date boundary.
- **Coverage:** Aggregate one closed session from 09:00 to 10:30 in UTC with UTC as the injected time zone.
- **Target effect:** One date/process bucket is returned with exactly 90 minutes; no other bucket is created.

#### B-02 — `Session_crossing_local_midnight_is_split_between_both_dates`
- **Purpose:** Prevent a cross-midnight session from being charged entirely to its start or end date.
- **Coverage:** Aggregate one session from 23:30 to 00:30 UTC across two consecutive local dates.
- **Target effect:** The first date receives 30 minutes and the second date receives 30 minutes; their sum equals the original 60-minute interval.

#### B-03 — `Session_ending_at_midnight_does_not_create_zero_duration_next_day_bucket`
- **Purpose:** Verify half-open interval semantics at the exact day boundary.
- **Coverage:** Aggregate a session ending exactly at 00:00 local time.
- **Target effect:** All duration belongs to the prior date and no zero-duration bucket exists for the ending date.

#### B-04 — `Aggregation_uses_injected_local_timezone_instead_of_UTC_date`
- **Purpose:** Ensure local calendar dates, not UTC dates, define daily reporting.
- **Coverage:** Aggregate a UTC interval whose UTC date differs from its date in an injected negative-offset time zone.
- **Target effect:** The bucket uses the expected local `DateOnly` value and its duration is unchanged.

#### B-05 — `Multiple_sessions_for_same_process_are_summed_without_merging_other_processes`
- **Purpose:** Verify the daily aggregation key is the local date plus process identity.
- **Coverage:** Feed two sessions for one process and one session for a second process on the same date.
- **Target effect:** The first process has the sum of its two durations; the second process has its own duration; no session duration is lost or double-counted.

#### B-06 — `Title_intervals_split_at_midnight_and_remain_separate_by_title`
- **Purpose:** Preserve useful title-level detail while applying the same day-boundary rules as app usage.
- **Coverage:** Feed title intervals for two titles, including one interval crossing local midnight, for the same process.
- **Target effect:** Each date/process/title bucket has the expected duration; different titles are not merged; app-level totals equal the sum of title detail where the inputs cover the same periods.

#### B-07 — `Open_title_interval_is_capped_at_explicit_as_of_time`
- **Purpose:** Include a currently open title interval without consulting wall-clock time or counting into the future.
- **Coverage:** Aggregate an interval with `EndedAt == null` and provide a fixed `asOfUtc` later than its start.
- **Target effect:** Duration ends exactly at `asOfUtc`; repeated calls with the same inputs are identical; no duration beyond `asOfUtc` is reported.

#### B-08 — `Spring_forward_day_uses_elapsed_UTC_duration_and_one_local_date`
- **Purpose:** Avoid inventing an hour when the local clock skips forward for daylight-saving time.
- **Coverage:** In `Eastern Standard Time`, aggregate 2026-03-08 06:30Z through 07:30Z, which crosses the local 01:30-to-03:30 clock change.
- **Target effect:** One local-date bucket contains exactly 60 minutes of elapsed usage; no nonexistent local hour is counted.

#### B-09 — `Fall_back_day_counts_repeated_local_hour_once_per_elapsed_interval`
- **Purpose:** Avoid losing or double-counting usage during the repeated local hour when clocks move back.
- **Coverage:** In `Eastern Standard Time`, aggregate 2026-11-01 05:30Z through 07:30Z, spanning the repeated 01:00 hour.
- **Target effect:** The one local-date bucket contains exactly 120 minutes, matching elapsed UTC time.

### C. Fifteen-local-day SQLite retention

#### C-01 — `Retention_cutoff_is_midnight_of_oldest_of_fifteen_local_dates`
- **Purpose:** Define “15 days” as 15 calendar dates including today, not a rolling 360-hour duration.
- **Coverage:** Given a fixed `now` and injected time zone, compute the start of today minus 14 local dates.
- **Target effect:** The returned cutoff is exactly local midnight for the oldest retained date converted to UTC.

#### C-02 — `Session_ending_exactly_at_cutoff_is_deleted_but_session_starting_at_cutoff_is_kept`
- **Purpose:** Lock down the inclusive retention boundary and half-open interval behavior.
- **Coverage:** Persist one session ending exactly at cutoff and another beginning exactly at cutoff.
- **Target effect:** The ended-at-cutoff interval is removed as wholly outside the retained window; the interval starting at cutoff remains unchanged.

#### C-03 — `Sessions_and_closed_titles_wholly_older_than_cutoff_are_deleted`
- **Purpose:** Prove cleanup removes stale raw telemetry from both supported tables.
- **Coverage:** Persist an old session and an old, closed title interval, each ending before cutoff, plus newer records.
- **Target effect:** Both old records disappear; all newer records remain unchanged.

#### C-04 — `Session_crossing_cutoff_is_clipped_and_keeps_only_retained_duration`
- **Purpose:** Preserve recent usage from a long session without retaining pre-window telemetry.
- **Coverage:** Persist a session starting before cutoff and ending after cutoff.
- **Target effect:** The same session ID/process/title remains, its `StartedAt` becomes exactly cutoff, its `EndedAt` is unchanged, and its retained duration is computed only from cutoff onward.

#### C-05 — `Closed_title_interval_crossing_cutoff_is_clipped_without_changing_its_end`
- **Purpose:** Apply the same retention rule to title-detail data.
- **Coverage:** Persist a closed title interval spanning cutoff.
- **Target effect:** `StartedAt` becomes cutoff; ID, process, title, and `EndedAt` remain unchanged.

#### C-06 — `Open_title_interval_crossing_cutoff_is_clipped_and_remains_open`
- **Purpose:** Keep a currently active title interval usable after retention cleanup.
- **Coverage:** Persist a title interval starting before cutoff with `EndedAt == null`.
- **Target effect:** `StartedAt` becomes cutoff and `EndedAt` stays null; subsequent upsert/close by the same ID still works.

#### C-07 — `Repeated_retention_cleanup_is_idempotent`
- **Purpose:** Make startup or scheduled cleanup safe to run more than once.
- **Coverage:** Run cleanup twice with the same cutoff against the same temporary database.
- **Target effect:** The second run changes no rows; all reads match the result after the first run.

#### C-08 — `Retention_cleanup_does_not_modify_unrelated_tables`
- **Purpose:** Prevent usage cleanup from deleting settings or other data in the same SQLite file.
- **Coverage:** Add a sentinel unrelated table/row to the temporary database, then run cleanup.
- **Target effect:** The sentinel schema and row remain byte-for-byte/value-for-value unchanged while eligible usage rows are cleaned.

#### C-09 — `Retention_cutoff_tracks_local_calendar_across_daylight_saving_change`
- **Purpose:** Prevent implementing 15 local dates as a fixed 15×24-hour subtraction.
- **Coverage:** Choose a local date range spanning the Eastern spring-forward transition and compute the cutoff from the local date/time zone.
- **Target effect:** The cutoff remains local midnight 14 calendar dates before today even though the corresponding UTC elapsed duration differs from 336 hours.

#### C-10 — `Clipped_timestamps_round_trip_as_UTC_instants`
- **Purpose:** Ensure date-boundary clipping does not change the instant through SQLite serialization.
- **Coverage:** Clip an interval using a non-UTC local zone, write/read it through the SQLite store, and compare instants.
- **Target effect:** Read-back start equals the UTC instant of cutoff; end remains the same UTC instant; no local offset is accidentally persisted as a shifted instant.

### D. DPI and multi-monitor pure geometry policy

#### D-01 — `One_hundred_percent_scale_is_identity`
- **Purpose:** Preserve existing placement on a standard-scale monitor.
- **Coverage:** Convert representative coordinates and sizes with scale 1.0.
- **Target effect:** Every value is unchanged exactly.

#### D-02 — `Dip_to_device_conversion_applies_explicit_non_integer_scale`
- **Purpose:** Verify logical-to-device conversion for a common non-integer DPI scale.
- **Coverage:** Convert 80 DIPs at scale 1.25.
- **Target effect:** The result is exactly 100 device pixels; scale is supplied as input and is not read from the OS.

#### D-03 — `Device_to_dip_conversion_applies_explicit_scale`
- **Purpose:** Verify the inverse conversion used to normalize monitor geometry.
- **Coverage:** Convert 300 device pixels at scale 1.5.
- **Target effect:** The result is exactly 200 DIPs.

#### D-04 — `Coordinate_round_trip_preserves_fractional_geometry`
- **Purpose:** Prevent systematic position/size drift after repeated conversions.
- **Coverage:** Round-trip representative fractional values at scales 1.25, 1.5, and 2.0.
- **Target effect:** The recovered values differ from the originals by no more than 1e-9 DIPs; rounding occurs only at the native integer boundary, not in the pure policy.

#### D-05 — `Negative_origin_secondary_monitor_remains_selectable_and_clampable`
- **Purpose:** Protect multi-monitor layouts where a monitor lies left of or above the primary display.
- **Coverage:** Resolve a saved secondary monitor with a negative work-area origin, then clamp a pet position in the same coordinate unit.
- **Target effect:** The selected monitor remains the requested secondary monitor and the clamped position stays within its work area while retaining the configured visible edge; no zero-origin assumption is applied.

#### D-06 — `Visible_edge_margin_is_twenty_physical_pixels_at_one_hundred_fifty_percent_scale`
- **Purpose:** Preserve the user-visible edge invariant when policy geometry is expressed in DIPs.
- **Coverage:** At scale 1.5, convert a 20-device-pixel minimum edge to its logical equivalent and clamp the pet at each work-area edge.
- **Target effect:** The resulting visible portion is at least 20 physical pixels on every tested edge within 1e-9 pixels in the pure-double policy; integer rounding is deferred to the native boundary. Using 20 DIPs directly must not accidentally produce a 30-pixel requirement.

#### D-07 — `Same_physical_visible_margin_is_independent_of_monitor_scale`
- **Purpose:** Ensure moving the pet between displays does not change the physical edge visibility requirement.
- **Coverage:** Apply the same 20-device-pixel margin at scale 1.0 and 1.5.
- **Target effect:** The logical margins differ by scale, but both clamp results expose 20 physical pixels within 1e-9 pixels.

Existing tests for a missing saved monitor falling back to the primary monitor and for the default 20-unit visible edge remain regression tests; they are not duplicated by the new DPI tests.

## Windows Integration Verification — Not Unit Tests

- **Session/power:** Verify native notification-window registration/unregistration and routing of real Windows session/power messages. Actual lock, suspend, hibernate, resume, and shutdown lifecycle behavior must be exercised in a Windows integration/manual harness; these events must not be simulated and reported as unit tests.
- **Monitor/DPI:** On an interactive Windows desktop, verify monitor enumeration, launch on a secondary display, movement between displays with different scaling, monitor removal/reconnection, and the actual WPF window's physical visible edge. Pure coordinate tests cannot prove Windows' per-monitor DPI behavior.

## File Boundaries (Proposed)

- `src/DesktopPuppy/Usage/SessionPowerEventRouter.cs`: normalized event-to-boundary routing only; no message-window or P/Invoke code.
- `src/DesktopPuppy/Usage/DailyUsageAggregator.cs`: pure time-zone-aware session/title aggregation with explicit `asOfUtc`.
- `src/DesktopPuppy/Usage/UsageSqliteStore.cs`: retention cutoff cleanup and interval clipping for usage tables only.
- `src/DesktopPuppy/Pet/Placement/DpiGeometryPolicy.cs`: pure scale conversion; native DPI discovery stays in `Native`.
- `tests/DesktopPuppy.Tests/Usage/SessionPowerEventRouterTests.cs`.
- `tests/DesktopPuppy.Tests/Usage/DailyUsageAggregatorTests.cs`.
- `tests/DesktopPuppy.Tests/Usage/UsageSqliteStoreRetentionTests.cs`.
- `tests/DesktopPuppy.Tests/Pet/DpiGeometryPolicyTests.cs` and focused additions to `PetPlacementPolicyTests.cs`.

## Compatibility and Constraints

- Preserve current tracker, collector, title-debounce, and placement behavior except where a listed test explicitly defines an extension.
- Persist timestamps as UTC instants; convert only for local-day aggregation and retention cutoff calculation.
- Do not add a production dependency solely for tests.
- Keep tests deterministic: no real sleeps, wall-clock dependence, live Win32 hooks, or live desktop interaction in unit tests.
- Do not modify user changes in the main checkout.

## Acceptance Criteria

- Every listed test is implemented or explicitly identified as an existing regression test; no planned case is silently omitted.
- Each new test is first observed failing for the intended missing behavior before its minimal production implementation is added.
- Unit tests pass, and the Release solution build completes with no warnings/errors.
- Existing Windows UI tests remain separate and pass on an interactive Windows desktop.
- Documentation distinguishes pure unit coverage from Windows integration verification.

