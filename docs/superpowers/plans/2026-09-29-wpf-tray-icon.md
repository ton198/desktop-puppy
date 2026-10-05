# WPF System Tray Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a WPF tray icon/menu that can show or hide the existing pet, open usage statistics, exit cleanly, and restore the existing hidden pet when a second instance is launched.

**Architecture:** Use `Hardcodet.NotifyIcon.Wpf` `TaskbarIcon` with a WPF context menu. Keep Shell integration behind `ITrayIconHost`; route user commands through a testable `TrayCommandService` to `ITrayApplicationActions`, while `App` owns the service and the existing `SingleInstanceGuard`. Keep `ShutdownMode.OnMainWindowClose`; hide with `PetWindow.Hide()` and exit by closing the main pet window.

**Tech Stack:** .NET 10 WPF, Hardcodet.NotifyIcon.Wpf (pinned to the verified stable package version), xUnit, WPF UI Automation for existing non-default UI suites.

**Spec:** `docs/superpowers/specs/2026-09-29-wpf-tray-icon-design.md`

## Global Constraints

- Use the community WPF tray library `Hardcodet.NotifyIcon.Wpf` and its `TaskbarIcon` control with a WPF `ContextMenu`.
- Do not implement `Shell_NotifyIcon` directly and do not add a WinForms tray menu in this step.
- Verify current stable package identity/version, license, .NET 10 WPF compatibility, icon/context-menu/disposal APIs, and Explorer-restart behavior before adding a package reference. If authoritative sources are unavailable, stop and request access or report that verification is blocked; do not rely on unverified cache contents.
- If a material compatibility, maintenance, or license issue is found, stop and report it; do not silently switch tray implementations.
- The initial icon is a temporary generic, original vector mark; do not use Snoopy or other unlicensed character art.
- The tray menu provides Show Desktop Puppy, Hide Desktop Puppy, Usage Statistics, and Exit Desktop Puppy; no additional click, balloon, or menu behavior is added.
- Hiding uses `Hide()`, never `Close()`, and leaves the process, usage tracking, and statistics window alive.
- Opening statistics from the tray does not implicitly show the pet.
- The existing close button and tray Exit use the normal WPF exit lifecycle; the second instance restores the existing hidden/minimized pet.
- If tray initialization fails, startup remains non-fatal, the pet stays visible, and no hide command is available.
- Create, handle, and dispose the tray host on the WPF dispatcher; commands during shutdown are ignored and disposal is idempotent.
- Every new test documents **Purpose**, **Coverage**, and **Target effect** separately.
- No automated test sends input to the active desktop, locks the workstation, changes display topology, or accesses the production usage database.
- Mark actual Shell tray interaction as manual acceptance until an isolated, safe UI Automation path is proven.
- At implementation time, use a managed isolated worktree based on the approved main commit; preserve all existing local modifications in the primary checkout.

---

## File Map

- `src/DesktopPuppy/DesktopPuppy.csproj` — add the verified, pinned Hardcodet package reference.
- `src/DesktopPuppy/Pet/Tray/TrayIconCommand.cs` — immutable command identity and command event arguments.
- `src/DesktopPuppy/Pet/Tray/ITrayApplicationActions.cs` — application actions requested by the tray (`ShowPet`, `HidePet`, `OpenStatistics`, `ExitApplication`).
- `src/DesktopPuppy/Pet/Tray/ITrayIconHost.cs` — tray-host event/start/disposal boundary independent of the Shell library.
- `src/DesktopPuppy/Pet/Tray/TrayCommandService.cs` — initialize host, route commands, expose availability, activate the existing pet on second launch, and dispose exactly once.
- `src/DesktopPuppy/Pet/Tray/HardcodetTrayIconHost.cs` — create the library `TaskbarIcon`, WPF `ContextMenu`, and temporary generic vector icon; convert menu clicks to `TrayIconCommand` events.
- `src/DesktopPuppy/Pet/Tray/TrayIconResources.xaml` — generic original vector icon resource, replaceable by future approved art.
- `src/DesktopPuppy/App.xaml.cs` — own tray service; implement window/statistics/exit actions; connect single-instance activation; keep startup alive if tray setup fails; dispose on exit.
- `tests/DesktopPuppy.Tests/Pet/TrayCommandServiceTests.cs` — fake-host/fake-action unit tests for routing, failure, second-instance activation, and disposal.
- `docs/implementation-status.md` — record implementation, exact automated results, and manual TRAY acceptance statuses.

## Task 1: Verify the selected library before changing dependencies

**Files:**
- Read and update: `docs/superpowers/specs/2026-09-29-wpf-tray-icon-design.md`

- [ ] **Step 1: Query the exact package on the official NuGet feed.** Run `dotnet package search Hardcodet.NotifyIcon.Wpf --exact-match --format json --source https://api.nuget.org/v3/index.json`. Confirm the result is the exact package and select the latest stable (non-prerelease) version. Expected: official feed returns an exact package identity and stable version.
- [ ] **Step 2: Verify the license.** Open the official NuGet package page and linked upstream license text. Record the license identifier and URL. If the text is unavailable or incompatible, stop and report; do not add a package.
- [ ] **Step 3: Verify current maintenance and target support.** Check the release history and supported target frameworks in authoritative NuGet/upstream metadata. Confirm the package can be used by `net10.0-windows` WPF without moving the application off its target framework.
- [ ] **Step 4: Verify the exact WPF API and resource behavior.** Check upstream documentation/source for `TaskbarIcon`, `IconSource`, WPF `ContextMenu`, and deterministic disposal. Record the exact API members and disposal pattern needed by Task 3.
- [ ] **Step 5: Verify Explorer/taskbar restart handling.** Check the documented upstream behavior for notification-area icon recreation after Explorer restarts. Record whether the library handles it automatically or documents an event/recreation step.
- [ ] **Step 6: Record a go/no-go decision.** Update the selected approach in the spec with package ID, stable version, license, authoritative URLs, compatibility evidence, API/disposal notes, and restart behavior. If any material compatibility, maintenance, or license issue remains, stop here and report it; do not add the package or silently substitute another library.
- [ ] **Step 7: Review and commit the metadata.** Run `git diff --check -- docs/superpowers/specs/2026-09-29-wpf-tray-icon-design.md`, inspect the exact diff, and commit only the spec metadata change as `docs: record tray library verification`.
## Task 2: Define and implement the command boundary with TDD

**Files:**
- Create: `src/DesktopPuppy/Pet/Tray/TrayIconCommand.cs`
- Create: `src/DesktopPuppy/Pet/Tray/ITrayApplicationActions.cs`
- Create: `src/DesktopPuppy/Pet/Tray/ITrayIconHost.cs`
- Create: `src/DesktopPuppy/Pet/Tray/TrayCommandService.cs`
- Create: `tests/DesktopPuppy.Tests/Pet/TrayCommandServiceTests.cs`

**Interfaces:**

```csharp
public enum TrayIconCommand
{
    ShowPet,
    HidePet,
    OpenStatistics,
    Exit
}

public sealed class TrayIconCommandEventArgs(TrayIconCommand command) : EventArgs
{
    public TrayIconCommand Command { get; } = command;
}

public interface ITrayIconHost : IDisposable
{
    event EventHandler<TrayIconCommandEventArgs>? CommandRequested;
    void Start();
}

public interface ITrayApplicationActions
{
    void ShowPet();
    void HidePet();
    void OpenStatistics();
    void ExitApplication();
}

public sealed class TrayCommandService : IDisposable
{
    public TrayCommandService(ITrayIconHost host, ITrayApplicationActions actions);
    public bool IsAvailable { get; }
    public void Start();
    public void ActivateExistingInstance();
    public void Dispose();
}
```

- [ ] **Step 1: Add failing facts with Purpose/Coverage/Target effect comments.** Add these exact cases:
  - `Start_success_makes_tray_available` — **Purpose:** expose hide/show only after a real host is initialized. **Coverage:** fake host starts successfully. **Target effect:** `IsAvailable` is true only after `Start` succeeds.
  - `Show_command_routes_to_existing_pet` — **Purpose:** restore the existing pet without constructing another window. **Coverage:** fake host raises Show once. **Target effect:** only `ShowPet` is routed.
  - `Hide_command_never_requests_application_exit` — **Purpose:** keep tracking alive when the pet is hidden. **Coverage:** fake host raises Hide. **Target effect:** only Hide is routed; `ExitApplication` is never called.
  - `Statistics_command_does_not_show_a_hidden_pet` — **Purpose:** allow statistics access without changing pet visibility. **Coverage:** fake pet begins hidden; host raises Statistics. **Target effect:** only `OpenStatistics` is routed.
  - `Exit_command_requests_normal_exit_once` — **Purpose:** provide one deterministic shutdown path. **Coverage:** host raises Exit twice, then Show. **Target effect:** exit action is requested once and later commands are ignored.
  - `Host_start_failure_is_nonfatal_and_blocks_tray_commands` — **Purpose:** never hide the pet when no reliable restore entry exists. **Coverage:** fake host throws from `Start`, then emits commands. **Target effect:** `IsAvailable` is false, no command is routed, and the service does not request hiding or exit.
  - `Second_instance_activation_restores_existing_pet_even_when_tray_unavailable` — **Purpose:** preserve single-instance recovery independent of tray availability. **Coverage:** invoke activation after Hide and after host-init failure. **Target effect:** `ShowPet` is routed once and no second host/window is created.
  - `Dispose_detaches_events_and_disposes_host_once` — **Purpose:** remove the Shell icon and callbacks safely at shutdown. **Coverage:** dispose twice, then make the fake host raise a command. **Target effect:** host disposal occurs once and no action runs after disposal.
- [ ] **Step 2: Run the focused tests to verify RED.** Run:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
& 'C:\Program Files\dotnet\dotnet.exe' test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~TrayCommandServiceTests' --verbosity minimal -p:NuGetAudit=false --maxcpucount:1 --nodeReuse:false
```

Expected: compilation fails because `TrayCommandService` and its command-boundary types do not exist yet; there must be no unrelated test failures. If this project requires a build rather than test compilation to expose the missing types, report that compiler failure as the RED evidence and proceed only after confirming it is caused by the new test references.
- [ ] **Step 3: Implement the command contracts and service.** Keep contracts independent of the Hardcodet library, WPF `Window`, SQLite, and Win32. Subscribe to the host event before starting it; mark available only after successful start. On host-start failure, unsubscribe, dispose the failed host once, stay unavailable, and write only a fixed safe diagnostic. Route commands only while available and not disposed. Make Exit one-shot and ignore later commands. `ActivateExistingInstance()` must call `ShowPet()` even if tray initialization failed.
- [ ] **Step 4: Run the focused suite GREEN.** Use the same focused command. Expected: all eight facts pass; no real tray icon or active desktop interaction occurs.
- [ ] **Step 5: Commit the tested command boundary.** Stage only the four new tray contract/service files and `TrayCommandServiceTests.cs`; commit as `feat: add tray command routing`.
## Task 3: Add the real WPF tray host and temporary icon

**Files:**
- Modify: `src/DesktopPuppy/DesktopPuppy.csproj`
- Create: `src/DesktopPuppy/Pet/Tray/HardcodetTrayIconHost.cs`
- Create: `src/DesktopPuppy/Pet/Tray/TrayIconResources.xaml`

- [ ] **Step 1: Add the exact version approved in Task 1.** Run `dotnet add src/DesktopPuppy/DesktopPuppy.csproj package Hardcodet.NotifyIcon.Wpf` only after the package identity/version/license/API check; verify the resulting `PackageReference` is explicit and pinned in `DesktopPuppy.csproj`.
- [ ] **Step 2: Add an original vector placeholder.** Define a small `DrawingImage` in `TrayIconResources.xaml` using a generic paw/puppy mark, with no Snoopy likeness or copied image. Give the resource a stable key `DesktopPuppyTrayIcon` and a 16x16-equivalent view box.
- [ ] **Step 3: Implement `HardcodetTrayIconHost.Start()`.** Create one `TaskbarIcon` on the WPF dispatcher, assign `ToolTipText="Desktop Puppy"`, the vector `IconSource`, and one WPF `ContextMenu`. Add menu items with visible Chinese labels `显示桌宠`, `隐藏桌宠`, `使用统计`, and `退出 Desktop Puppy`, in the approved order with a separator before Exit. Each click raises exactly one corresponding `TrayIconCommandEventArgs`.
- [ ] **Step 4: Implement disposal and restart behavior.** Unsubscribe all `MenuItem.Click` handlers before disposing the `TaskbarIcon`. Verify against the Task 1 library metadata whether the control handles Explorer restart; if upstream requires explicit recreation, implement only its documented taskbar-created recovery mechanism inside this host. Do not put shell P/Invokes in `App` or `PetWindow`.
- [ ] **Step 5: Build the application.** Run `dotnet build src/DesktopPuppy/DesktopPuppy.csproj --configuration Release --no-restore --no-incremental --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false -p:NuGetAudit=false`. Expected: successful Release build with no new WPF/XAML or package compatibility errors.
- [ ] **Step 6: Commit the host.** Stage only the project file and the new tray-host/resource files; commit as `feat: add WPF tray icon host`.

## Task 4: Wire tray commands into application lifecycle

**Files:**
- Modify: `src/DesktopPuppy/App.xaml.cs`
- Test: `tests/DesktopPuppy.Tests/Pet/TrayCommandServiceTests.cs`

- [ ] **Step 1: Create tray service only after acquiring the single-instance guard.** Start it before showing the pet; if initialization reports unavailable, continue normal startup and leave the pet visible. Never create a tray icon in the second process.
- [ ] **Step 2: Implement `ITrayApplicationActions` in the WPF application boundary.** `ShowPet()` marshals to the dispatcher, shows a hidden window, restores `WindowState.Normal` if minimized, and activates it. `HidePet()` calls `Hide()` only. `OpenStatistics()` calls the existing statistics-window activation path and does not show the pet. `ExitApplication()` closes the main `PetWindow` so `ShutdownMode.OnMainWindowClose` and existing cleanup remain authoritative.
- [ ] **Step 3: Connect second-instance activation to the service action.** Keep `SingleInstanceGuard` as the sole instance owner. Its callback dispatches `TrayCommandService.ActivateExistingInstance()` onto the WPF dispatcher; confirm the path restores the existing pet when hidden or minimized and never constructs a second `PetWindow` or tray host.
- [ ] **Step 4: Dispose the service on exit before tearing down WPF resources.** Ensure the service is detached and disposed once in `App.OnExit`; keep usage/power/database cleanup behavior unchanged.
- [ ] **Step 5: Run focused tests and a serialized app build.** Run the focused test command from Task 2 and the Release app build from Task 3. Expected: all focused command tests pass and Release compilation succeeds; do not run UI automation that injects active-desktop input.
- [ ] **Step 6: Commit the lifecycle wiring.** Stage only `src/DesktopPuppy/App.xaml.cs` and directly related tray test changes; commit as `feat: wire tray into app lifecycle`.

## Task 5: Full regression, status record, and operator acceptance checklist

**Files:**
- Modify: `docs/implementation-status.md`

- [ ] **Step 1: Run the complete deterministic unit suite.** Run:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
& 'C:\Program Files\dotnet\dotnet.exe' build DesktopPuppy.sln --configuration Release --no-restore --no-incremental --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false -p:NuGetAudit=false
& 'C:\Program Files\dotnet\dotnet.exe' test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-build --no-restore --verbosity minimal -p:NuGetAudit=false --maxcpucount:1 --nodeReuse:false
```

Expected: serialized Release solution build succeeds and the full deterministic unit suite has 0 failed/0 skipped. Record actual totals and warnings; do not claim zero warnings if external NuGet audit metadata is unavailable.
- [ ] **Step 2: Do not run unsafe desktop automation by default.** Do not run `Category=Ui` or `SendInput` interaction tests on the active desktop. Do not launch the visible tray manual acceptance without the operator present.
- [ ] **Step 3: Add a manual acceptance section to the implementation status.** Include TRAY-01, TRAY-02, TRAY-03 with separate Purpose/Coverage/Target effect, mark each **Not run / Manual required**, and provide concise operator actions matching the spec. Unit tests and build do not change these statuses.
- [ ] **Step 4: Review implementation scope and diff.** Confirm only tray source/tests, the app/project files, the status file, and the approved spec/plan are changed. Run `git diff --check`; preserve all unrelated user modifications.
- [ ] **Step 5: Commit the status update.** Stage only `docs/implementation-status.md`; commit as `docs: record tray implementation status`.
- [ ] **Step 6: Prepare the user handoff.** Report unit/build results and identify TRAY-01–03 as requiring the user's manual acceptance. Do not report them as passed until the user provides results.

## Spec Coverage Self-Review

- Package identity, version, license, .NET 10 compatibility, APIs, disposal, and Explorer restart behavior: Task 1.
- Selected WPF library, WPF context menu, generic replaceable icon, no direct Shell P/Invoke: Task 3.
- Show/hide/statistics/exit semantics, initialization failure, dispatcher ownership, single-instance restore, and disposal: Tasks 2–5.
- Purpose/Coverage/Target effect tests: Task 2.
- Real notification-area behavior and all manual-only cases: Task 5; remain Not run until operator execution.
- Release build, complete unit suite, documentation and diff review: Task 5.
