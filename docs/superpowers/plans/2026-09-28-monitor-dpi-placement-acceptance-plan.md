# Native Monitor, DPI Placement, and Display Acceptance Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Connect real monitor work areas and DPI to the production pet window placement path, then verify mixed-DPI positioning and monitor-removal recovery on suitable Windows hardware.

**Architecture:** Keep Win32 monitor enumeration, physical window bounds, and DPI queries inside Native adapters. An injectable display provider reports monitor geometry; an injectable native window adapter reports a real HWND's bounds, containing monitor, and DPI. A testable placement service applies the existing policy in physical pixels, while PetWindow forwards lifecycle/DPI/location events. Deterministic unit tests cover service rules; visible opt-in acceptance flows cover real hardware and require manual topology changes.

**Tech Stack:** .NET 10 WPF, Win32 monitor/window APIs, xUnit 2.9.2, UI Automation, existing `DpiGeometryPolicy` and `PetPlacementPolicy`.

**Spec:** `docs/superpowers/specs/2026-09-28-windows-e2e-acceptance-design.md` sections 3.3, 4, 5 E2E-DISPLAY-01/E2E-DISPLAY-02, 6, 7, 8, and 9.

## Global Constraints

- Monitor enumeration, HWND geometry, and `GetDpiForWindow` P/Invoke live in `src/DesktopPuppy/Native`; WPF code-behind contains no Win32 declarations.
- Monitor bounds, work areas, and HWND rectangles use physical screen pixels at the Native boundary; WPF dimensions use DIPs and convert using the DPI reported for the real window on its current display.
- `SetWindowPos` uses `SWP_NOACTIVATE | SWP_NOZORDER`; placement correction must never steal foreground or alter the pet window topmost state.
- Minimum visible edge remains 20 physical pixels; when a display disappears, fallback uses `PetPlacementPolicy` and the still-connected primary monitor.
- No test changes resolution, scaling, topology, power plan, or connected hardware. Monitor removal is manual and only in the opt-in acceptance utility. Skip before creating the isolated run database if another `DesktopPuppy` process is already running, because the product single-instance guard would otherwise activate it and invalidate the isolated-app observation.
- Acceptance is opt-in on an interactive Windows desktop. The mixed-DPI scenario reports `Skipped` unless two monitors with distinct actual `GetDpiForWindow` values are observed on the same test HWND.
- Monitor IDs in reports are anonymous run-local labels; never persist or print full hardware/device identifiers.
- Every added test and acceptance case has **Purpose**, **Coverage**, and **Target effect**; mere visibility is insufficient to pass topology recovery.
- Preserve user changes and update `docs/implementation-status.md` only with freshly verified results.

---

## File Map

- `src/DesktopPuppy/Native/PhysicalRect.cs` — integer physical-pixel rectangle.
- `src/DesktopPuppy/Native/MonitorDisplayInfo.cs` — physical monitor bounds/work area and in-memory device ID.
- `src/DesktopPuppy/Native/IMonitorDisplayProvider.cs` — injectable enumeration and display-change notification.
- `src/DesktopPuppy/Native/IWin32DisplayApi.cs` and `Win32DisplayApi.cs` — fakeable seam for native display enumeration calls; `Win32DisplayApi.cs` contains the internal native monitor-info mapping record.
- `src/DesktopPuppy/Native/Win32MonitorDisplayProvider.cs` — monitor enumeration mapping and `SystemEvents.DisplaySettingsChanged` adapter.
- `src/DesktopPuppy/Native/IWin32WindowApi.cs` and `Win32WindowApi.cs` — fakeable seam for native HWND calls.
- `src/DesktopPuppy/Native/INativeWindowPositioner.cs` and `Win32WindowPositioner.cs` — `GetWindowRect`, `GetDpiForWindow`, monitor lookup, and `SetWindowPos` adapter.
- `src/DesktopPuppy/Pet/Placement/PetWindowPlacementService.cs` — policy application over injected monitor/window adapters; no WPF or P/Invoke; caches the last valid request by monitor ID so OS relocation cannot overwrite the removed-display position.
- `src/DesktopPuppy/Pet/Placement/PetPlacementPolicy.cs` and `DpiGeometryPolicy.cs` — explicit physical-pixel/DIP contracts and tested visibility rules.
- `src/DesktopPuppy/Pet/PetWindow.xaml.cs` — forward HWND creation, location, DPI, display-change and close lifecycle to the placement service.
- `src/DesktopPuppy/App.xaml.cs` — compose adapters/service and inject into PetWindow; preserve optional startup behavior.
- `src/DesktopPuppy/DesktopPuppy.csproj` and `src/DesktopPuppy/app.manifest` — set per-monitor-v2 DPI awareness before WPF creates an HWND.
- `tests/DesktopPuppy.Tests/Native/MonitorDisplayContractTests.cs` — rectangle/display contract checks.
- `tests/DesktopPuppy.Tests/Native/Win32MonitorDisplayProviderTests.cs` — fake native enumeration, mapping, and event-unsubscription checks.
- `tests/DesktopPuppy.Tests/Native/Win32WindowPositionerTests.cs` — fake HWND bounds, monitor selection, and per-window DPI checks.
- `tests/DesktopPuppy.Tests/Pet/PetWindowPlacementServiceTests.cs` — deterministic placement, topology fallback, idempotency, and event lifecycle.
- `tests/DesktopPuppy.Tests/PetPlacementPolicyTests.cs` and `tests/DesktopPuppy.Tests/Pet/DpiGeometryPolicyTests.cs` — retain and clarify pure policy guarantees.
- `tests/DesktopPuppy.WindowsAcceptance/DisplayAcceptanceScenario.cs` — real mixed-DPI placement and operator-guided topology removal.
- `tests/DesktopPuppy.WindowsAcceptance/WindowsAcceptanceWindow.xaml` and `.xaml.cs` — visible opt-in, prerequisites, instructions and result states.
- `tests/DesktopPuppy.WindowsAcceptance/AcceptanceRun.cs` — isolated app process/DB, UIA HWND lookup, real monitor observation and safe diagnostics.
- `tests/DesktopPuppy.WindowsAcceptance/README.md` — display prerequisites and manual topology-removal steps.
- `docs/implementation-status.md` — report unit automation separately from physical display results.

## Task 1: Define Physical Monitor and Native Window Contracts

**Files:**
- Create: `src/DesktopPuppy/Native/PhysicalRect.cs
- Create: `src/DesktopPuppy/Native/MonitorDisplayInfo.cs
- Create: `src/DesktopPuppy/Native/IMonitorDisplayProvider.cs
- Create: `src/DesktopPuppy/Native/INativeWindowPositioner.cs
- Create: `tests/DesktopPuppy.Tests/Native/MonitorDisplayContractTests.cs

**Interfaces:**

```csharp
public readonly record struct PhysicalRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsValid => Right > Left && Bottom > Top;
}

public sealed record MonitorDisplayInfo
{
    public MonitorDisplayInfo(string deviceId, PhysicalRect bounds, PhysicalRect workArea, bool isPrimary)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("Device ID is required.", nameof(deviceId));
        if (!bounds.IsValid) throw new ArgumentOutOfRangeException(nameof(bounds));
        if (!workArea.IsValid) throw new ArgumentOutOfRangeException(nameof(workArea));
        DeviceId = deviceId;
        Bounds = bounds;
        WorkArea = workArea;
        IsPrimary = isPrimary;
    }
    public string DeviceId { get; }
    public PhysicalRect Bounds { get; }
    public PhysicalRect WorkArea { get; }
    public bool IsPrimary { get; }
}

public interface IMonitorDisplayProvider
{
    IReadOnlyList<MonitorDisplayInfo> GetMonitors();
    event EventHandler? DisplaySettingsChanged;
}

public interface INativeWindowPositioner
{
    PhysicalRect GetBounds(IntPtr hwnd);
    MonitorDisplayInfo GetMonitorForWindow(IntPtr hwnd);
    uint GetDpi(IntPtr hwnd);
    void SetBounds(IntPtr hwnd, PhysicalRect bounds);
}


**Tests:**
1. `PhysicalRect_reports_extents_for_negative_virtual_coordinates
   - **Purpose:** Prevent monitors left/above the primary display from being treated as invalid.
   - **Coverage:** Negative left/top with right/bottom beyond origin.
   - **Target effect:** Width and height are exact positive physical-pixel extents.
2. `PhysicalRect_marks_empty_or_inverted_bounds_invalid
   - **Purpose:** Keep invalid native geometry out of placement math.
   - **Coverage:** Equal and reversed rectangle edges.
   - **Target effect:** `IsValid` is false and `MonitorDisplayInfo` rejects invalid work areas.
3. `MonitorDisplayInfo_keeps_physical_bounds_and_primary_flag
   - **Purpose:** Preserve native monitor geometry without losing which display is primary.
   - **Coverage:** Valid bounds/work area and primary flag.
   - **Target effect:** Immutable record preserves each value.

- [ ] **Step 1: Write the three contract tests and confirm RED.** Expected failure is the missing types/validation behavior; no test calls native monitor APIs.
- [ ] **Step 2: Implement the physical rectangle and display contracts.** Keep full device ID in memory only; logs use a per-run alias. Validate positive bounds and work-area dimensions.
- [ ] **Step 3: Run focused tests.** Run `dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --filter "FullyQualifiedName~MonitorDisplayContractTests"`; expected: all three pass without attached-monitor assumptions.

## Task 2: Implement and Test Native Enumeration and HWND Observation

**Files:**
- Create: `src/DesktopPuppy/Native/IWin32DisplayApi.cs` and `Win32DisplayApi.cs
- Create: `src/DesktopPuppy/Native/IWin32WindowApi.cs` and `Win32WindowApi.cs
- Create: `tests/DesktopPuppy.Tests/Native/Win32MonitorDisplayProviderTests.cs
- Create: `tests/DesktopPuppy.Tests/Native/Win32WindowPositionerTests.cs

**Interfaces:** `IWin32DisplayApi.EnumerateMonitors(Func<IntPtr, bool> callback)` returns success/failure and calls the callback once per native monitor handle; `TryGetMonitorInfo(IntPtr monitorHandle, out NativeMonitorInfo info)` returns bounds/work area in physical pixels plus the primary flag. `IWin32WindowApi.TryGetWindowRect(IntPtr hwnd, out PhysicalRect bounds)`, `GetMonitorFromWindow(IntPtr hwnd)`, `GetDpiForWindow(IntPtr hwnd)`, and `SetWindowPosition(IntPtr hwnd, PhysicalRect bounds)` are fakeable native-call seams; the Win32 implementation uses `SWP_NOACTIVATE | SWP_NOZORDER`, and failed calls are surfaced, not replaced with defaults. `Win32MonitorDisplayProvider.GetMonitors()` maps these results and throws a clear exception on enumeration failure or missing primary. `Win32WindowPositioner.GetDpi(hwnd)` calls `GetDpiForWindow` on the real HWND; it does not use system DPI as a substitute. Display-change callbacks forward `SystemEvents.DisplaySettingsChanged` and are unsubscribed on `Dispose()`.

**Tests:**
1. `Provider_maps_primary_secondary_and_negative_origin_monitors
   - **Purpose:** Ensure Windows' enumeration order does not dictate primary selection or discard left-side monitors.
   - **Coverage:** Fake monitor handles with one primary and one negative-origin secondary.
   - **Target effect:** Both work areas and the primary flag map correctly.
2. `Provider_reports_native_enumeration_failure_without_fabricating_a_monitor
   - **Purpose:** Avoid silently positioning from invented geometry.
   - **Coverage:** Native enumeration failure and no-primary response.
   - **Target effect:** A clear failure is returned to the caller; no default rectangle is treated as a monitor.
3. `Provider_unsubscribes_display_change_handler_on_dispose
   - **Purpose:** Avoid retaining windows or receiving callbacks during shutdown.
   - **Coverage:** Subscribe, dispose, then raise a fake display change.
   - **Target effect:** No callback reaches a disposed placement client.
4. `Window_positioner_reads_physical_bounds_monitor_and_actual_dpi
   - **Purpose:** Keep HWND measurements in one physical coordinate/DPI context.
   - **Coverage:** Fake `GetWindowRect`, monitor lookup and `GetDpiForWindow` responses.
   - **Target effect:** Returned values match the native adapter output and zero/failed DPI is surfaced as failure.

- [ ] **Step 1: Add `IWin32DisplayApi` and `IWin32WindowApi` seams and all four fake-driven adapter tests.** The display seam exposes callback enumeration of monitor handles and retrieval of one monitor-info structure; the window seam exposes `GetWindowRect`, `MonitorFromWindow`, `GetDpiForWindow`, and `SetWindowPos` results. Do not enumerate or mutate connected displays in unit tests.
- [ ] **Step 2: Implement `EnumDisplayMonitors` / `GetMonitorInfo` mapping and Win32 HWND observation.** Keep P/Invoke declarations in the two Native adapters. Use `GetDpiForWindow` for the live window DPI; do not substitute a system-wide DPI value for a secondary display.
- [ ] **Step 3: Add disposable display-change forwarding.** Subscribe once, marshal no WPF work from the native callback thread, unsubscribe idempotently.
- [ ] **Step 4: Run focused tests and build.** Run filters for `Win32MonitorDisplayProviderTests` and `Win32WindowPositionerTests`, then `dotnet build src/DesktopPuppy/DesktopPuppy.csproj --configuration Release`. Expected: all fake tests pass and no new platform analyzer warnings.

## Task 3: Add the Placement Service and Deterministic Behavior Tests

**Files:**
- Create: `src/DesktopPuppy/Pet/Placement/PetWindowPlacementService.cs
- Create: `tests/DesktopPuppy.Tests/Pet/PetWindowPlacementServiceTests.cs
- Modify: `src/DesktopPuppy/Pet/Placement/PetPlacementPolicy.cs
- Modify: `src/DesktopPuppy/Pet/Placement/DpiGeometryPolicy.cs
- Modify: `tests/DesktopPuppy.Tests/PetPlacementPolicyTests.cs` and `tests/DesktopPuppy.Tests/Pet/DpiGeometryPolicyTests.cs

**Interface:** `PetWindowPlacementService(IMonitorDisplayProvider monitors, INativeWindowPositioner windows)` exposes `ApplyInitial(IntPtr hwnd, double widthDip, double heightDip)`, `Reconcile(IntPtr hwnd, double widthDip, double heightDip)`, `Forget(IntPtr hwnd)`, and `Dispose()`. It operates on HWND identity and physical rectangles only; PetWindow owns the WPF dispatcher and forwards lifecycle events. Convert WPF DIP sizes with the live `GetDpi(hwnd) / 96.0`, then convert them to physical integers using `Math.Ceiling`; round physical left/top using `MidpointRounding.AwayFromZero`, and clamp the final integer rectangle to a 20-physical-pixel visible edge. Convert `PhysicalRect` and `MonitorDisplayInfo.WorkArea` into the existing `MonitorWorkArea`/`PetPosition` policy types at the service boundary, then convert the result back to `PhysicalRect`. Cache the last valid physical request by monitor ID while that monitor is still enumerated; if that monitor later disappears, do not replace its cached request with an OS-relocated HWND rectangle. Resolve the still-connected primary display, recompute window size at its actual HWND DPI, and clamp the cached position there. Reconcile is idempotent and skips `SetBounds` when no correction is required.

**Tests:**
1. `Initial_placement_clamps_window_inside_primary_work_area
   - **Purpose:** Keep the first real pet window accessible when WPF startup placement is off-screen or under taskbar bounds.
   - **Coverage:** Fake HWND bounds and primary work area.
   - **Target effect:** Physical target rectangle matches policy and preserves the 20-pixel visible edge.
2. `Placement_uses_actual_window_dpi_for_physical_size
   - **Purpose:** Avoid assuming one display scale for every WPF window.
   - **Coverage:** Same DIP size at 100% and 150%, with actual HWND DPI supplied by the fake adapter.
   - **Target effect:** Physical extents use measured DPI and the visible edge remains 20 physical pixels.
3. `Topology_change_moves_pet_from_missing_monitor_to_primary_fallback
   - **Purpose:** Ensure a disconnected display cannot strand the pet.
   - **Coverage:** Cached secondary-monitor request followed by enumeration without that display.
   - **Target effect:** Bounds equal the policy-clamped cached request on the primary work area.
4. `Repeated_reconcile_does_not_issue_redundant_native_moves
   - **Purpose:** Prevent move-notification feedback loops.
   - **Coverage:** Two reconciliations when actual bounds are already valid.
   - **Target effect:** No additional `SetBounds` call occurs.
5. `Monitor_selection_falls_back_to_primary_when_previous_device_is_missing
   - **Purpose:** Preserve a deterministic recovery monitor when the prior device disappears.
   - **Coverage:** Unknown prior ID plus one current primary display.
   - **Target effect:** Placement selects the primary monitor.
6. `Forget_discards_cached_state_when_pet_window_closes
   - **Purpose:** Prevent stale monitor identity/coordinates leaking into a later window lifetime.
   - **Coverage:** Apply placement, call `Forget(hwnd)`, then start a second HWND.
   - **Target effect:** The new HWND uses its own geometry and does not reuse the old pet position.

- [ ] **Step 1: Add the six service/policy tests and confirm RED.** Use fake monitor/window adapters; tests do not instantiate or move WPF windows.
- [ ] **Step 2: Clarify policy units and preserve existing pure-policy coverage.** Policy inputs are physical-pixel work areas, physical HWND width/height and physical visible-edge margin; `DpiGeometryPolicy` converts WPF DIPs to device pixels using the measured window DPI.
- [ ] **Step 3: Implement the service.** Handle primary/secondary selection, clamp, cached physical request, removed-monitor fallback and idempotency; add no settings persistence or unrelated PRD features.
- [ ] **Step 4: Run all placement and geometry tests.** Expected: prior edge/negative-origin tests and all six new service/policy cases pass without hardware.

## Task 4: Enable Per-Monitor DPI and Wire the Production WPF Window

**Files:**
- Create: `src/DesktopPuppy/app.manifes
- Modify: `src/DesktopPuppy/DesktopPuppy.csproj
- Modify: `src/DesktopPuppy/App.xaml.cs
- Modify: `src/DesktopPuppy/Pet/PetWindow.xaml.cs

- [ ] **Step 1: Add `PerMonitorV2` awareness before any WPF HWND is created.** Add the manifest value plus supported legacy fallback and attach it in the project file. Do not change awareness through a runtime P/Invoke after WPF starts.
- [ ] **Step 2: Compose and inject adapters/service.** App owns provider/service and passes them into PetWindow; HWND initialization stores the handle; `Loaded` applies initial placement after WPF dimensions are available; location/DPI events call `Reconcile`; display notifications are marshalled to the WPF dispatcher; window close unsubscribes and calls `Forge
- [ ] **Step 3: Preserve safe startup and current initial intent.** Use existing centered startup as the starting request, then clamp it against the primary physical work area. If native monitor initialization fails, leave the pet on WPF's existing placement and continue normal app startup.
- [ ] **Step 4: Run Release build, full unit tests, and existing UI smoke tests.** Confirm drag, interaction, and statistics-window behavior remain intact; inspect that all P/Invokes remain under Native.

## Task 5: Add the Opt-In Mixed-DPI Live Acceptance Scenario

**E2E-DISPLAY-01 — Mixed DPI placement**
- **Purpose:** Prove the real WPF pet placement path uses actual display geometry and DPI conversion rather than only pure policy tests.
- **Coverage:** At least two connected displays; the same Release app HWND manually dragged by the operator to each display; `GetDpiForWindow`, physical bounds, containing monitor, work area and edge-clamp observations read-only from the acceptance utility.
- **Target effect:** Distinct HWND DPI values are observed; the pet remains on the requested target display with policy-compliant physical bounds and at least 20 visible pixels. If DPI values are equal, report `Skipped` with the measured reason.

**Prerequisite:** Complete Tasks 1 and 2 of `docs/superpowers/plans/2026-09-28-windows-session-power-acceptance-plan.md` first; this scenario extends that visible acceptance utility and process lifecycle.

**Files:**
- Create: `tests/DesktopPuppy.WindowsAcceptance/DisplayAcceptanceScenario.cs
- Modify: `tests/DesktopPuppy.WindowsAcceptance/WindowsAcceptanceWindow.xaml` and `.xaml.cs
- Modify: `tests/DesktopPuppy.WindowsAcceptance/AcceptanceRun.cs
- Modify: `tests/DesktopPuppy.WindowsAcceptance/README.md

- [ ] **Step 1: Add explicit opt-in and safe prerequisites.** Require `DESKTOP_PUPPY_WINDOWS_ACCEPTANCE=1` and `DESKTOP_PUPPY_DISPLAY_ACCEPTANCE=1`, a local interactive desktop and two enumerated monitors; otherwise display `Skipped` with reason before starting the app or changing window placement.
- [ ] **Step 2: Check actual DPI on the same test HWND.** Launch the app with a unique temporary database; ask the operator to drag the same pet HWND to each display and confirm each observation in the visible utility. Read `GetDpiForWindow` and physical bounds after WPF processes each move. The production location/DPI path must perform reconciliation; the acceptance utility must not call `SetWindowPos` to manufacture the tested placement. Equal values report `Skipped`; no display settings are changed.
- [ ] **Step 3: Assert the real WPF placement result.** On distinct DPI hardware, compare actual HWND monitor and physical rectangle against the policy result; verify work-area edge, 20-pixel visible margin and live process. Use UIA/native rectangle only, not screenshot pixels.
- [ ] **Step 4: Sanitize result and verify the filters.** Log run-local monitor aliases, DPI values and geometry only. Confirm standard unit/UI test commands do not launch this standalone utility; only the explicit opt-in launch runs hardware acceptance.

## Task 6: Extend the Acceptance Utility for Manual Topology Removal

**E2E-DISPLAY-02 — Monitor removal recovery**
- **Purpose:** Prove the app re-evaluates a pet window whose display disappears and applies the documented primary fallback.
- **Coverage:** Pet on secondary display; operator manually disconnects/disables it; actual display-change event; refreshed monitor enumeration; captured pre-change request compared with primary-work-area policy result.
- **Target effect:** Actual HWND bounds match the expected fallback within 1 physical pixel, remain at least 20 physical pixels visible, and the app remains running. Visibility alone does not pass.

**Files:**
- Modify: `tests/DesktopPuppy.WindowsAcceptance/DisplayAcceptanceScenario.cs
- Modify: `tests/DesktopPuppy.WindowsAcceptance/WindowsAcceptanceWindow.xaml` and `.xaml.cs
- Modify: `tests/DesktopPuppy.WindowsAcceptance/AcceptanceRun.cs` and `README.md

- [ ] **Step 1: Add visible manual instructions and preconditions.** Require `DESKTOP_PUPPY_WINDOWS_ACCEPTANCE=1` and `DESKTOP_PUPPY_DISPLAY_ACCEPTANCE=1`, local desktop, and two monitors. Identify a run-local target as `Display 2`, ask the operator to drag the real test pet there, and tell the operator to manually disconnect/disable only that display through Windows controls. No automated topology changes.
- [ ] **Step 2: Observe real topology change with bounded wait.** Cache run-local target/primary aliases, the actual physical pet rectangle, and the current primary work area in the acceptance scenario before the operator removes the display. Do not depend on exposing internal service cache state. Keep dispatcher responsive; wait up to five minutes for actual `DisplaySettingsChanged` plus fresh enumeration showing the target absent. The service must retain the last valid request for the removed monitor even if Windows moves the HWND before the notification is processed. Timeout/cancel is `Incomplete`; unmet prerequisites are `Skipped`.
- [ ] **Step 3: Compare exact fallback placement.** Calculate expected physical width/height from the pet WPF DIP dimensions and the live HWND DPI after WPF processes the DPI change; clamp the pre-removal physical left/top and those extents to the still-connected primary work area with `PetPlacementPolicy.Clamp`. Compare this independently calculated rectangle with the actual HWND bounds; do not derive the expected result from the post-change bounds. Require all physical edges to match within 1 pixel, primary membership, 20 visible pixels and live process.
- [ ] **Step 4: Report and clean up safely.** Show anonymous monitor/DPI data and test result. Never restore the topology automatically; ask the operator to restore the display manually. Close app and remove only the acceptance run's owned database directory.

## Task 7: Verify and Update Implementation Status

**Files:**
- Modify: `docs/implementation-status.md

- [ ] **Step 1: Run Release build, full unit tests, and existing UI smoke tests.** Then run the mixed-DPI scenario only with opt-in. Record a hardware skip with its reason rather than counting it as pass.
- [ ] **Step 2: Run topology removal only with the operator present.** If not performed, leave it `Not run`; never infer hardware success from fake adapter/service tests.
- [ ] **Step 3: Record exact evidence.** Document production connection, unit/UI results, actual environment prerequisites, whether real display removal was performed, and still-unrun manual verification.
- [ ] **Step 4: Review safety and cleanup.** Confirm no display mutation API exists, P/Invoke remains in Native, event subscriptions are detached, monitor failure does not terminate normal startup, and no unrelated files changed.

## Completion Checklis

- [ ] Monitor data and HWND bounds are physical pixels; actual DPI comes from `GetDpiForWindow` on the real HWND and all WPF conversion is tested.
- [ ] Startup, cross-screen DPI change and topology loss use the same placement/visibility rule.
- [ ] Mixed-DPI acceptance proves monitor assignment, actual HWND DPI and physical placement; unsatisfied prerequisites report `Skipped` with reason.
- [ ] Topology acceptance observes a real manual change and checks exact policy fallback, not just visibility.
- [ ] Default tests do not change display state or block for operator actions.
