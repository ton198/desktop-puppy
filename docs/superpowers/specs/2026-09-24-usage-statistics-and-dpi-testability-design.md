# Usage Statistics and DPI Testability Design

## Goal

Add trustworthy automated coverage for the next foreground-usage and monitor-placement behaviors by extracting testable core policies, while keeping OS and WPF integration out of ordinary unit tests.

## Current State

The `codex/foreground-usage-slice` branch already has unit coverage for foreground session tracking (including lock, sleep, hibernate, and shutdown boundaries), title debouncing, collector composition, SQLite round-trips, and basic monitor selection/clamping. The uncovered work is the native session/power-event bridge, local-day usage aggregation, 15-local-day retention, and DPI-aware geometry. Real Win32 registration, monitor enumeration, and WPF scaling require Windows integration checks rather than simulated unit tests.

## Design

### 1. Power/session event boundary

Introduce a small testable event-routing boundary between Windows session/power notifications and `ForegroundSessionTracker`. It maps lock, suspend/hibernate, and shutdown notifications to the existing `SessionBoundary` values. Unlock/resume notifications do not synthesize a foreground session; collection resumes on the next foreground-window event. Unit tests use a fake event source/router input to verify mapping and forwarding. The actual message window, P/Invoke registration, and delivery by Windows remain integration concerns.

### 2. Local-day usage aggregation

Add a pure aggregator over persisted, closed foreground sessions and title intervals. It uses an injected `TimeZoneInfo`, treats intervals as half-open `[start, end)`, splits intervals at local-midnight boundaries, and reports duration per local date and process (with title detail for title intervals). Tests cover a same-day interval, crossing midnight, an interval ending exactly at midnight, and a daylight-saving transition. No system clock or database access belongs in the aggregator.

### 3. Fifteen-local-day retention

Define the retained window as the current local date plus the previous 14 local dates. Compute its cutoff from local midnight in an injected time zone. SQLite cleanup applies only to usage-session and title-interval tables. Rows wholly before the cutoff are removed; intervals crossing the cutoff are clipped to the cutoff so no pre-window telemetry remains while the in-window portion is preserved. Tests use a temporary SQLite database and cover the exact cutoff, older rows, crossing intervals (including an open title interval), and preservation of unrelated tables/data.

### 4. DPI geometry policy

Keep native monitor/DPI discovery outside the unit-test project. Add a pure coordinate conversion policy with an explicit DPI scale and tests for logical/device-pixel conversion, round trips, monitor selection fallback, and visible-edge clamping at non-100% scales. Existing native enumeration and actual WPF per-monitor behavior will be validated separately on Windows.

## Compatibility and Constraints

- Preserve existing `ForegroundSessionTracker`, `UsageCollector`, and `PetPlacementPolicy` behavior unless a new test explicitly defines an extension.
- Keep SQLite timestamps in UTC; only aggregation/retention boundaries use the injected local time zone.
- Do not add a production dependency solely for tests.
- Keep tests deterministic: no real sleeps, wall-clock dependence, Win32 hooks, or live desktop interaction in unit tests.
- Do not modify user changes in the main checkout.

## Acceptance Criteria

- Each new core behavior has tests written and observed failing before its minimal implementation is added.
- The unit suite passes after implementation; a Release solution build has no warnings/errors.
- Existing Windows UI tests remain separate and pass when run on an interactive Windows desktop.
- Documentation clearly distinguishes unit tests from Windows integration verification.
