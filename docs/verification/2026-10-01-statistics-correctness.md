# Statistics correctness verification — 2026-10-01

## Scope and source identity

Approved bounded fixes: foreground-title sampling, first foreground sample, and conservative recovery of abandoned title intervals. Base commit: `ec1c241` on `main`; source/test edits remain uncommitted. Existing user README, design, and plan edits are preserved.

## Implemented behavior

- The existing 250 ms background advance samples the current foreground window before promoting a title. Only a synchronous successful observation permits advancement; a refresh returning without a snapshot cannot promote stale pending text.
- Sampling does not clear lock/suspend pause state. Explicit resume/unlock still owns reopening collection.
- Startup samples the current foreground window immediately after the watcher starts and the collector becomes active.
- Stable titles carry an optional UTC last-confirmed timestamp. The initial durable interval uses the last actual observation; subsequent checkpoints are throttled to 30 seconds and keep the same interval ID.
- Timer-path failures are contained with fixed non-throwing diagnostics; failed title creation/checkpoint/close keeps retryable debounce state. Failed closes retain the original observed boundary separately from live state and are persisted before the same title can reopen, preventing lock/suspend downtime from being folded into a retry. Titles and raw exceptions are not added to these diagnostics.
- SQLite initialization transactionally adds the nullable checkpoint column to legacy databases, but does not close another connection's active titles.
- After acquiring the single-instance guard, the application explicitly recovers abandoned titles before retention and collection. Recovery seals open rows to the last checkpoint, or the start for legacy/uncheckpointed rows, and leaves normally closed rows unchanged.
- Checkpoint/close upserts preserve a start already advanced by retention, rather than restoring expired history.

## Automated evidence

- RED: three sampling/startup regressions failed on the original implementation.
- RED: checkpoint throttling and five recovery cases failed before checkpoint/recovery implementation.
- RED: live-observer initialization and retention/checkpoint interaction failed before their integration fixes.
- RED: missing-observation advancement, checkpoint exception containment, and failed title-close retry failed before failure-path fixes. A stronger original-boundary assertion and same-title lock/resume regression also failed before retaining a separate pending closed interval.
- GREEN: complete deterministic unit suite **212 passed, 0 failed, 0 skipped**, up from 193 (19 additional cases).
- Serialized non-incremental Release solution build: **exit 0, 0 errors, 5 warnings** (3 NU1900 unavailable vulnerability-data-source warnings and 2 WFO0003 high-DPI manifest warnings).
- Separate Windows UI test project build: **exit 0, 0 errors, 1 NU1900 warning**.
- Selected non-input-injecting desktop UI suite: **9 passed, 0 failed, 0 skipped** (23 seconds, final rerun). Filter: `FullyQualifiedName~DesktopPuppyWindowUiTests|FullyQualifiedName~UsageStatisticsWindowUiTests|Category=LongRunningE2E`. This covers window startup/normal close, statistics behavior/retention, and the real 30-second refresh timer. Test instances used private databases; no existing product instance was present. Mouse/keyboard input was not injected.
- The first selected UI run had 8 passes and 1 failure: a retention test assumed the whole database had only its seeded row. Immediate foreground sampling legitimately adds current-day rows. The assertion was corrected to verify the exact seeded IDs, cutoff, and expired-row absence; the complete selected suite then passed 9/9.
- The complete input-injecting UI suite and all manual Shell/OS/display acceptance were **Not run**. No lock, suspend, display change, or Explorer restart was initiated.

Commands:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.dotnet-home'
dotnet build DesktopPuppy.sln --configuration Release --no-restore --no-incremental --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false -p:NuGetAudit=false
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-build --no-restore --maxcpucount:1 --nodeReuse:false -p:NuGetAudit=false --verbosity minimal
dotnet build tests/DesktopPuppy.UiTests/DesktopPuppy.UiTests.csproj --configuration Release --no-restore --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false -p:NuGetAudit=false
```

NuGetAudit=false avoids requesting a fresh online audit in these build commands; existing cached NU1900 warnings remain visible. This is not a successful dependency vulnerability scan.

## Review

A read-only independent code review identified missing-observation promotion, uncontained checkpoint failures, and original-boundary loss on failed close. Regression failures were observed before fixes. Final review of the latest sources reported no remaining actionable findings within the approved scope; it did not itself execute tests or manual acceptance.

## Boundaries and remaining work

- The approximately 30-second loss bound concerns title detail under normally scheduled, successfully persisted sampling. Timer delays, failed writes, or unavailable observations can increase loss.
- Active foreground sessions are still persisted when closed. An abrupt process termination can lose the active application session; this change is not complete crash recovery of usage totals. Confirmed title detail and application totals can consequently differ after a crash.
- Polling detects observed changes, not every sub-250 ms transition. This is not a native title-change Hook implementation.
- Startup/shutdown still contain synchronous database operations; the previously identified Dispatcher I/O debt is not addressed here.
- Actual tray, second-process recovery, lock/power notifications, mixed-DPI display transitions, removal, and Explorer restart require the operator checklist. No manual scenario is marked passed from the automated suite.
