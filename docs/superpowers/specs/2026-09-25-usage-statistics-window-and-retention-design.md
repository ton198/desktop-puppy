# Usage Statistics Window and 15-Day Retention Design

**Date:** 2026-09-25
**Status:** Approved in chat; awaiting written-spec review
**Scope:** Desktop Puppy foreground-usage statistics and runtime data retention

## Goal

Let the user inspect locally collected foreground-application usage without changing the pet's compact, always-on-top window, while retaining only the latest 15 local calendar dates of usage data.

## User-visible behavior

- Add a **Statistics** entry to the pet window. It opens a separate, modeless statistics window.
- The statistics window defaults to today and allows choosing any of the latest 15 local calendar dates (today plus the previous 14 dates).
- Show total foreground time for the selected date and an application ranking ordered by duration, longest first.
- Selecting an application shows its window-title usage details for the selected date, ordered by duration.
- Load or refresh when the window opens, when it is activated, and when its selected date changes. While open, refresh every 30 seconds so ongoing usage continues to accrue. A refresh uses one UTC `asOf` instant; ongoing intervals are counted only up to that instant.
- If a selected date has no records, say so explicitly. If statistics cannot be read, show an unavailable/error state rather than presenting a misleading zero; this must not prevent the pet from running.
- No charts, export, cloud sync, or AI summaries are included in this slice.

## Data flow and component boundaries

1. `UsageCollector` exposes an immutable, read-only snapshot of its current foreground application session, synchronized with its existing internal gate. The snapshot is not persisted just to refresh the statistics screen.
2. A statistics query captures one UTC `asOf` instant and obtains the current-session snapshot under the collector gate before reading completed sessions and title intervals from `UsageSqliteStore`.
3. It merges the active snapshot and stored sessions by their stable session ID, then caps durations at `asOf` before calling the existing `DailyUsageAggregator` with the current time zone. This ordering and deduplication avoid both double-counting and a temporary gap if a session closes during a query. Open title intervals are already capped by the aggregator at `asOf`.
4. The query returns date-specific total, application-ranking, and title-detail data to the independent WPF window. Selecting a different date or application changes only the displayed slice; it does not change collection behavior.
5. Database work runs away from the UI thread. Results are applied on the dispatcher only if they still correspond to the latest requested date, so a slower earlier request cannot overwrite newer selection state. Refresh work is limited to the statistics window's lifetime.

The pet remains usable if optional usage tracking or database access is unavailable. Existing UTC interval storage and local-time-zone-aware aggregation remain the sources of truth; durations are elapsed UTC time split at actual local-day boundaries.

## Retention behavior

- Retain exactly the current local date and the previous 14 local dates. Derive the cutoff from the start of the oldest retained local date, converted to UTC through `UsageRetentionPolicy`.
- Reuse `UsageSqliteStore.PruneBefore`, which removes expired usage rows and clips intervals crossing the cutoff in a transaction, without touching unrelated tables.
- Invoke cleanup after database initialization at application startup and once after each subsequent local-date change. Recalculate the cutoff when cleanup runs so waking from sleep or a delayed timer catches up. Do not run a database cleanup on every foreground polling tick.
- Cleanup failure is non-fatal to the pet. Transactional rollback preserves the previous complete database state; a later date-change check can retry. Statistics read failures are reported in the statistics window and retried on the next normal refresh/activation.

## Error and lifecycle handling

- Keep the statistics window independent of the pet's size and topmost behavior. Closing the statistics window stops its refresh timer; reopening it loads current data again.
- Do not block the WPF dispatcher during SQLite reads or aggregation. Ignore/cancel stale refresh results after a newer date selection or window close.
- Empty data is a normal state, distinct from a database or tracking failure.
- Usage collection is optional. Failure to initialize it must not prevent pet startup; the statistics window should explain that statistics are currently unavailable rather than crash or report zero.

## Test goals

### Unit tests

- Verify a current in-memory application session contributes only through `asOf`, then moves to persisted-session input without being omitted or counted twice during a close/query race.
- Verify daily totals and title details for selected dates, application ordering, title aggregation, and local-midnight/DST splitting using the existing aggregator and injected clock/time zone.
- Verify the 15-local-date cutoff at date boundaries and across daylight-saving changes; expired records are removed, overlapping records are clipped, active title intervals survive, and cleanup is idempotent.
- Verify the statistics query distinguishes empty results from read failures and does not let an older asynchronous result replace a newer date selection.
- Verify cleanup is requested after initialization and once per local date (including after delayed execution/resume), not on each foreground polling tick.

### Windows UI tests

- Verify the pet's Statistics entry opens the separate statistics window and that the pet remains available.
- Verify date selection, application-to-title-detail selection, empty/error states, and refresh lifecycle behavior.
- Verify closing/reopening the window stops/restarts its refresh lifecycle and that usage-tracking startup failure does not prevent the pet from launching.

Automated UI tests do not replace manual validation of live foreground collection and Windows session/power event delivery.

## Existing implementation to reuse

- `UsageRetentionPolicy` already computes the 15-local-date UTC cutoff.
- `UsageSqliteStore.PruneBefore` already performs transactional deletion and clipping.
- `DailyUsageAggregator` already splits intervals by local date and caps open title intervals at `asOfUtc`.
- The application initializes the usage store and collector at startup, but does not yet invoke retention or provide a statistics consumer.

## Acceptance criteria

1. The user can open a separate statistics window from the pet, view today's time/ranking, switch across the retained 15 dates, and inspect title detail for a selected application.
2. The active foreground application's time is represented through the query's `asOf` instant and refreshes every 30 seconds while the statistics window is open.
3. Runtime cleanup enforces the 15-local-date boundary at startup and after local-date changes while preserving boundary-crossing data correctly.
4. Empty data and operational errors are distinguishable, and statistics failures do not terminate the pet.
5. The specified unit and Windows UI tests pass, alongside the existing regression suites.
