# Task 6 Report — Manual Monitor-Removal Acceptance

## Status

**Implemented and verified in the isolated Task 6 worktree.** This report is for Task 6 only. The base before dispatch was `45748743f71f2198bfb612a4752eebae093e863e` (Task 5 review clean). The worktree branch is `codex/windows-e2e-acceptance`.

## Summary of implementation

- Extended the existing `DisplayAcceptanceScenario` and its read-only Native adapter boundary to capture a pre-removal physical HWND rectangle, target and primary run-local aliases, the primary work area, and the display-event generation. Monitor aliases remain bound to their run-local monitor mapping across refreshes and are never replaced in reports with hardware identifiers.
- Added manual E2E-DISPLAY-02 instructions, preflight checks, and a confirmation flow. The utility requires both acceptance opt-ins, a local interactive desktop, and at least two displays; Display 2 must be a secondary display. Existing `DesktopPuppy` processes cause a skip before the owned run database is created. The operator—not the utility—moves the pet and disconnects/restores the display.
- The controller waits asynchronously for a real `DisplaySettingsChanged` event and fresh enumeration showing the captured target absent, bounded by five minutes. It confirms that the captured primary remains connected and primary, then waits for stable observation of the original HWND.
- Corrected the transient-enumeration failure path. Once a post-capture event has been seen, a separate persistent `displayChangeObserved` state remains true even when a fresh `RefreshMonitors()` call throws. The loop retries enumeration at intervals of at most 250 ms until the target is absent or the bounded deadline expires; a second display event is not required. This prevents the updated generation counter from accidentally suppressing retries.
- Fallback is calculated independently from the **captured pre-removal physical left/top** and captured primary work area. The expected physical width/height come from the pet's 260×210 WPF-DIP size and the live `GetDpiForWindow` value after the topology/DPI change. `PetPlacementPolicy.Clamp` applies the 20-physical-pixel visibility rule. Acceptance requires the original HWND and live isolated process, target absence, captured primary membership, all four actual HWND edges within one physical pixel, and at least 20 physical pixels visible. Post-change bounds do not redefine the expected placement.
- Cleanup continues to stop only the owned app and delete only its owned database after exit is confirmed. Failure to confirm exit retains the directory. No topology restoration is automated.
- Added/extended focused fake-Native tests for fallback calculation and rejection boundaries, the real-event/fresh-enumeration flow, transient enumeration failure after that same event, stable run-local aliases, timeout without a real event, and cancellation. Each acceptance test has Purpose, Coverage, and Target effect annotations.

## TDD evidence

The Task 6 progress ledger records the behavioral RED and GREEN runs. In particular, the event-refresh regression RED reproduced the reported risk: after one real event and one transient enumeration exception, the scenario returned **Incomplete** at the deadline instead of continuing to validate the recovered topology. The GREEN change preserves the event-seen flag independently of the generation value and retries without another event. The new focused regression was freshly rerun after implementation and passed **1/1**.

Other Task 6 acceptance validation cases also had recorded RED/GREEN cycles in the progress ledger: the fallback assertion initially rejected the intended policy result, and a reordered topology initially reassigned the removed display's alias to a different monitor. Both focused cases were corrected and passed.

## Verification performed

- Focused transient-enumeration regression: `dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~Monitor_removal_retries_transient_enumeration_failure_after_same_event` — **1 passed, 0 failed, 0 skipped**.
- Fresh serialized Release solution build: `dotnet build DesktopPuppy.sln --configuration Release --no-restore --no-incremental --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false` — **succeeded, 0 warnings, 0 errors**.
- Fresh full deterministic test project: `dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-build --no-restore --verbosity minimal` — **174 passed, 0 failed, 0 skipped**.
- `git diff --check` — clean.
- Self-review confirmed this change reuses Task 5 acceptance/native boundaries. No new monitor, HWND geometry, or DPI P/Invokes were added to WPF code-behind. The acceptance controller does not invoke placement or topology mutation APIs.
- `docs/implementation-status.md` was not changed. No Task 7 work was done.

## Not run / remaining hardware evidence

- `Category=Ui` was **Not run** because its tests inject input into the active desktop.
- The Windows acceptance utility was **not launched**.
- Manual E2E-DISPLAY-02 acceptance is **Not run**. No display was disconnected or disabled; no resolution, scaling, power plan, topology, or connected hardware was changed.

Consequently, the fake-backed regression and build establish the controller behavior under the simulated transient-enumeration path, but do **not** establish actual operating-system topology recovery or live HWND placement on hardware. A qualifying operator-run manual acceptance remains the only outstanding evidence.

## Concerns

No unresolved implementation issue was found in the scoped self-review. The live monitor-removal path remains unverified on real hardware by design and must continue to be reported **Not run** until performed manually.