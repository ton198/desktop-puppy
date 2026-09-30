# WinForms Tray Icon Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a .NET WinForms `NotifyIcon` tray entry point to the existing WPF app so the user can hide/restore the pet, open usage statistics while it stays hidden, exit normally, and reactivate the same pet from a second launch.

**Architecture:** Keep WPF as the only application/lifetime owner and run the WinForms tray components on the existing WPF Dispatcher; do not create a WinForms window or message loop. Put tray commands behind framework-neutral interfaces and a testable command service, while a small WinForms host owns the icon, context menu, menu items, and icon resource and is disposable exactly once.

**Tech Stack:** .NET 10 Windows Desktop SDK, WPF, built-in Windows Forms `NotifyIcon`/`ContextMenuStrip`, xUnit; no new third-party NuGet package.

**Spec:** `docs/superpowers/specs/2026-09-29-wpf-tray-icon-design.md`

## Global Constraints

- Keep `TargetFramework` at `net10.0-windows` and enable both `<UseWPF>true</UseWPF>` and `<UseWindowsForms>true</UseWindowsForms>`; do not add a third-party tray package.
- WPF remains the sole application framework and lifecycle owner; do not create a WinForms top-level window, call `System.Windows.Forms.Application.Run`, or call `Shell_NotifyIcon` directly.
- Construct, access, receive callbacks from, and dispose WinForms tray components on the WPF Dispatcher thread.
- Use one menu in this order: `Show Desktop Puppy`, `Hide Desktop Puppy`, `Usage Statistics`, separator, `Exit Desktop Puppy`; add no extra click or balloon behavior.
- Hiding calls `PetWindow.Hide()` and never closes the window or exits the process; statistics can open while the pet remains hidden; Exit and the existing close button use the normal WPF shutdown path.
- Start the tray only in the process that successfully acquires `SingleInstanceGuard`; second-instance activation restores the existing pet and never constructs a second pet or tray host.
- On tray initialization failure, keep startup non-fatal and the pet visible, leave tray actions unavailable, and emit only a fixed safe diagnostic without window titles, usage data, exception text, or credentials.
- `NotifyIcon.Visible` is not proof that Explorer registered the icon. Do not claim Shell presence from it; keep real icon/menu and Explorer-restart behavior in manual acceptance.
- The tray host owns the `NotifyIcon`, `ContextMenuStrip`, each generated menu item, and its loaded original generic multi-resolution `.ico`; detach events, clear owned references, and dispose each owned component/resource exactly once.
- No automated test may inject input into the active desktop, restart Explorer, lock/suspend the workstation, change display topology, or access the production usage database.
- Every new test states **Purpose**, **Coverage**, and **Target effect** separately in its own source comments.
- Preserve the already dirty primary checkout; implementation and this plan belong to the existing isolated branch `codex/wpf-tray-icon`.

---

## File Map

- `src/DesktopPuppy/DesktopPuppy.csproj` — enable the Windows Forms desktop framework alongside WPF and embed the original tray `.ico` resource.
- `tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj` — enable Windows Forms only if needed for direct deterministic menu-factory tests; keep the test target Windows-specific.
- `src/DesktopPuppy/Pet/Tray/TrayIconCommand.cs` — enum and event args for the four tray commands.
- `src/DesktopPuppy/Pet/Tray/ITrayApplicationActions.cs` — framework-independent actions implemented by `App`.
- `src/DesktopPuppy/Pet/Tray/ITrayIconHost.cs` — start/event/disposal seam used by the command service.
- `src/DesktopPuppy/Pet/Tray/TrayCommandService.cs` — start availability, failure handling, safe command routing, second-instance activation, and one-time disposal.
- `src/DesktopPuppy/Pet/Tray/WinFormsTrayIconHost.cs` — create/configure/dispose the built-in WinForms tray components on the dispatcher.
- `src/DesktopPuppy/Pet/Tray/TrayContextMenuFactory.cs` — create the approved menu labels/order and map menu clicks to command identities without registering an icon.
- `src/DesktopPuppy/Properties/AssemblyInfo.cs` — expose the internal menu factory only to `DesktopPuppy.Tests` for deterministic STA tests.
- `src/DesktopPuppy/Pet/Tray/Assets/DesktopPuppyTray.ico` — original generic multi-resolution tray mark; no Snoopy or copied character art.
- `src/DesktopPuppy/App.xaml.cs` — compose and own the tray service/actions, reuse statistics activation, and route existing single-instance activation.
- `src/DesktopPuppy/Pet/Tray/ITrayPetWindow.cs` — framework-independent visibility/window-action contract.
- `src/DesktopPuppy/Pet/Tray/TrayApplicationActions.cs` — testable show/hide/statistics/exit behavior using the pet-window contract.
- `tests/DesktopPuppy.Tests/Pet/Tray/TrayApplicationActionsTests.cs` — fake-window tests for app-action semantics.
- `tests/DesktopPuppy.Tests/Pet/Tray/TrayCommandServiceTests.cs` — fake host/action tests for service lifecycle and routing.
- `tests/DesktopPuppy.Tests/Pet/Tray/TrayContextMenuFactoryTests.cs` — optional STA-only tests for menu structure/click mapping; no `NotifyIcon`, Shell registration, or visible UI.
- `docs/implementation-status.md` — record exact build/test results and TRAY-01–04 manual status/evidence.

## Task 1: Enable WPF and WinForms together and keep names unambiguous

**Files:**
- Modify: `src/DesktopPuppy/DesktopPuppy.csproj`
- Modify: `src/DesktopPuppy/App.xaml.cs`
- Modify: `src/DesktopPuppy/Pet/PetWindow.xaml.cs` — explicitly select WPF event-argument types shadowed by the new WinForms framework imports.

**Interfaces:**
- Consumes: existing `App : System.Windows.Application`, `System.Threading.Timer`, and .NET 10 Windows Desktop target.
- Produces: the same app with both desktop frameworks referenced, and explicit aliases for ambiguous type names.

- [ ] **Step 1: Add the Windows Forms framework property.** In the application property group add exactly `<UseWindowsForms>true</UseWindowsForms>` adjacent to `<UseWPF>true</UseWPF>`. Do not add a tray NuGet `PackageReference`.
- [ ] **Step 2: Make framework-colliding identities explicit.** In `App.xaml.cs`, add `using WpfApplication = System.Windows.Application;` and `using Timer = System.Threading.Timer;`, then change the declaration to `public partial class App : WpfApplication`; retain the WPF namespace import for `Window`/`WindowState`. In `PetWindow.xaml.cs`, add aliases `WpfDpiChangedEventArgs = System.Windows.DpiChangedEventArgs`, `WpfMouseEventArgs = System.Windows.Input.MouseEventArgs`, and `WpfButton = System.Windows.Controls.Button`; use them in `PetWindow_OnDpiChanged`, `PetWindow_OnMouseMove`, and each `e.OriginalSource is Button` check. This preserves the intended WPF event handling and button filtering while both frameworks are enabled.
- [ ] **Step 3: Restore the solution for the dual-framework project.** Run:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
& 'C:\Program Files\dotnet\dotnet.exe' restore DesktopPuppy.sln --ignore-failed-sources -p:NuGetAudit=false
```

Expected: restore succeeds using the existing packages/Windows targeting packs, with no added third-party package.
- [ ] **Step 4: Build before adding tray code.** Run:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build src/DesktopPuppy/DesktopPuppy.csproj --configuration Release --no-restore --no-incremental --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false -p:NuGetAudit=false
```

Expected: current app compiles with WPF and WinForms references enabled; resolve only actual name/reference errors before proceeding.
- [ ] **Step 5: Review the exact diff and commit.** Run `git diff --check`, verify no package reference was added, then commit only the project file and the WPF/Threading aliases in `App.xaml.cs` and `PetWindow.xaml.cs` as `build: enable WinForms alongside WPF`.

## Task 2: Specify the command service with fake-backed tests (TDD)

**Files:**
- Create: `src/DesktopPuppy/Pet/Tray/TrayIconCommand.cs`
- Create: `src/DesktopPuppy/Pet/Tray/ITrayApplicationActions.cs`
- Create: `src/DesktopPuppy/Pet/Tray/ITrayIconHost.cs`
- Create: `src/DesktopPuppy/Pet/Tray/TrayCommandService.cs`
- Create: `tests/DesktopPuppy.Tests/Pet/Tray/TrayCommandServiceTests.cs`

**Interfaces:**

```csharp
public enum TrayIconCommand { ShowPet, HidePet, OpenStatistics, Exit }

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

- [ ] **Step 1: Write eight facts first.** Put an independent three-line comment above each `[Fact]` using the exact labels **Purpose**, **Coverage**, and **Target effect**. Name and assert each case as follows:

| Fact name | Purpose | Coverage | Target effect |
|---|---|---|---|
| `Start_success_makes_tray_available` | Expose tray actions only after a host initializes successfully. | A fake host completes `Start` without error. | `IsAvailable` changes from false to true only after successful start, and repeated `Start` does not create a second host start. |
| `Show_command_routes_to_existing_pet` | Restore the existing pet rather than constructing another window. | A started fake host raises Show once. | `ShowPet` is called once; Hide, Statistics, and Exit are not called. |
| `Repeated_hide_requests_never_exit_the_application` | Keep collection running when the pet is hidden. | A started host raises Hide twice. | `HidePet` is called twice; no Show or Exit action is called. |
| `Statistics_command_does_not_show_a_hidden_pet` | Open statistics without changing pet visibility. | Fake actions begin with the pet hidden; host raises Statistics. | Only `OpenStatistics` runs and the fake pet remains hidden. |
| `Exit_command_requests_normal_exit_once_and_ignores_later_commands` | Make shutdown deterministic and prevent commands racing with shutdown. | Host raises Exit twice and then Show. | `ExitApplication` is called once; later commands have no effect. |
| `Host_start_failure_is_nonfatal_and_blocks_tray_commands` | Never offer Hide without a working restore entry. | Fake host throws from `Start` and then attempts to raise a command. | Start does not escape the failure, `IsAvailable` stays false, host is detached/disposed once, and no action runs. |
| `Second_instance_activation_restores_existing_pet_even_when_tray_unavailable` | Keep single-instance recovery independent of tray setup. | Start fails, then `ActivateExistingInstance` is called twice. | `ShowPet` is called twice against the same fake action/window identity; host start remains one attempt. |
| `Dispose_detaches_events_and_disposes_host_once` | Remove callbacks/resources safely at shutdown. | Dispose twice, then raise a fake-host command. | Host is disposed once and no action is invoked after disposal. |

Use a fake host exposing `StartCount`, `DisposeCount`, a configurable `StartException`, and `Raise(TrayIconCommand)`; use fake application actions recording each call and a mutable `PetVisible` flag. The test source must state that these fakes do not prove Shell registration.
- [ ] **Step 2: Run the focused tests and capture RED.** Run:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
& 'C:\Program Files\dotnet\dotnet.exe' test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~TrayCommandServiceTests' --verbosity minimal -p:NuGetAudit=false --maxcpucount:1 --nodeReuse:false
```

Expected: compilation fails because the new command contracts/service do not exist; no unrelated test failure is accepted as RED evidence.
- [ ] **Step 3: Implement the minimal command boundary.** Keep it independent from WinForms, WPF `Window`, SQLite, and Win32. Subscribe before starting. Set availability only after success. Make `Start` idempotent. On a thrown start, detach the callback, attempt one host disposal, remain unavailable, and emit only a constant safe diagnostic (never append exception details). Route commands only while available, not disposed, and not exiting. Mark Exit as requested before invoking `ExitApplication`. `ActivateExistingInstance` calls `ShowPet` even when startup failed, unless shutdown/disposal has started.
- [ ] **Step 4: Run the focused tests and capture GREEN.** Re-run the exact test command from Step 2. Expected: all eight facts pass with no UI/Shell/database activity.
- [ ] **Step 5: Review and commit the service boundary.** Run `git diff --check`; commit only the five Task 2 files as `feat: add testable tray command service`.

## Task 3: Add the owned WinForms host, context menu, and generic icon

**Files:**
- Modify: `src/DesktopPuppy/DesktopPuppy.csproj`
- Modify: `tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj` — enable Windows Forms for deterministic STA tests.
- Create: `src/DesktopPuppy/Properties/AssemblyInfo.cs` — grant `InternalsVisibleTo("DesktopPuppy.Tests")` only.
- Create: `src/DesktopPuppy/Pet/Tray/TrayContextMenuFactory.cs`
- Create: `src/DesktopPuppy/Pet/Tray/TrayResourceCleanup.cs` — run ordered best-effort cleanup actions so one failing release does not strand later resources.
- Create: `src/DesktopPuppy/Pet/Tray/WinFormsTrayIconHost.cs`
- Create: `src/DesktopPuppy/Pet/Tray/Assets/DesktopPuppyTray.ico`
- Create: `tests/DesktopPuppy.Tests/Pet/Tray/TrayContextMenuFactoryTests.cs`
- Create: `tests/DesktopPuppy.Tests/Pet/Tray/TrayResourceCleanupTests.cs`

**Interfaces:**
- Consumes: `ITrayIconHost`, `TrayIconCommand`, `TrayIconCommandEventArgs` from Task 2.
- Produces: `WinFormsTrayIconHost : ITrayIconHost`; internal `TrayContextMenuFactory.Create(Action<TrayIconCommand>)` returns an unattached `ContextMenuStrip` with commands in spec order; internal `TrayResourceCleanup.Run(params Action[] cleanupSteps)` invokes each cleanup action in input order and continues after individual failures.

- [ ] **Step 1: Enable Windows Forms in the test project and write all three tests first.** Add `<UseWindowsForms>true</UseWindowsForms>` to the test project. Add three facts, each with separate `// Purpose:`, `// Coverage:`, and `// Target effect:` comments:
  - `Menu_has_approved_labels_and_order` constructs the factory menu on an STA worker thread and asserts the actual sequence: `Show Desktop Puppy`, `Hide Desktop Puppy`, `Usage Statistics`, separator, `Exit Desktop Puppy`.
  - `Clicking_each_menu_item_emits_its_matching_command_once` constructs the menu on an STA thread, calls `PerformClick()` on each menu item, and asserts the literal command sequence `[ShowPet, HidePet, OpenStatistics, Exit]` and exactly four commands.
  - `Cleanup_continues_after_one_action_throws` passes two recording actions to `TrayResourceCleanup.Run`; the first appends `1` then throws, and the second appends `2`; assert cleanup returns and the exact recorded sequence is `[1, 2]`.
  The STA helper creates a dedicated thread, sets `ApartmentState.STA` before starting, captures/rethrows exceptions, and joins. Tests create no `NotifyIcon`, do not set `Visible=true`, display no menu, and send no active-desktop input.
- [ ] **Step 2: Restore and prove RED.** Run:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
& 'C:\Program Files\dotnet\dotnet.exe' restore DesktopPuppy.sln --ignore-failed-sources -p:NuGetAudit=false
& 'C:\Program Files\dotnet\dotnet.exe' test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~TrayContextMenuFactoryTests|FullyQualifiedName~TrayResourceCleanupTests' --verbosity minimal -p:NuGetAudit=false --maxcpucount:1 --nodeReuse:false
```

Expected: restore succeeds with available framework packs; the test build fails only because `TrayContextMenuFactory` and `TrayResourceCleanup` have not yet been implemented. No actual Shell or desktop interaction occurs.
- [ ] **Step 3: Implement the context-menu factory and best-effort cleanup helper.** `TrayContextMenuFactory` creates the exact four labels/order, inserts one `ToolStripSeparator` before Exit, attaches one click handler per command, and exposes `DetachHandlers` that removes those handlers and clears its callback reference. `TrayResourceCleanup.Run` iterates the supplied actions in order, catches each exception independently, emits only fixed safe text (no exception details), and continues to the next action.
- [ ] **Step 4: Run focused tests GREEN.** Run the focused test command from Step 2. Expected: all three tests pass, including continued cleanup after a simulated first-action failure.
- [ ] **Step 5: Create the original generic multi-resolution icon asset.** Add `Assets/DesktopPuppyTray.ico` with an original neutral paw/puppy mark containing 16×16 and 32×32 representations. Do not use Snoopy/copied character art, the executable icon, or `SystemIcons`. Verify the ICO directory has both image entries; use Windows `System.Drawing.Icon` to load and render both requested sizes, and record the actual dimensions/results. Embed it in `DesktopPuppy.csproj` with stable manifest name `DesktopPuppy.Pet.Tray.Assets.DesktopPuppyTray.ico`.
- [ ] **Step 6: Implement `WinFormsTrayIconHost.Start()` on the WPF Dispatcher.** Verify dispatcher access; load the embedded resource into an owned `System.Drawing.Icon`; create and assign `_notifyIcon = new FormsNotifyIcon()` before configuring any property; create one `ContextMenuStrip`; set icon, tooltip `Desktop Puppy`, context menu, and `Visible=true`; bridge menu commands to `CommandRequested`. Do not start a WinForms message loop. If any stage fails, clean all partially-created owned objects and rethrow so the command service can leave tray unavailable.
- [ ] **Step 7: Implement idempotent disposal with explicit ownership and best-effort cleanup.** Verify dispatcher access before mutation, set `_disposed` before cleanup, copy owned resources to locals, and clear host fields so each component/resource is attempted at most once. Pass independent ordered actions to `TrayResourceCleanup.Run`: detach menu handlers, set icon `Visible=false`, clear its `ContextMenuStrip`, dispose `NotifyIcon`, dispose `ContextMenuStrip` (which owns its menu items), and dispose the loaded `Icon`. A failure in one action must not skip any later release. Use a clear fixed exception if no WPF `Application.Current` exists; never report exception detail.
- [ ] **Step 8: Build and commit.** Run:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build src/DesktopPuppy/DesktopPuppy.csproj --configuration Release --no-restore --no-incremental --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false -p:NuGetAudit=false
& 'C:\Program Files\dotnet\dotnet.exe' test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~TrayContextMenuFactoryTests|FullyQualifiedName~TrayResourceCleanupTests' --verbosity minimal -p:NuGetAudit=false --maxcpucount:1 --nodeReuse:false
```

Expected: app build succeeds; all three focused tests pass without Shell interaction. Record warnings exactly; do not claim the Shell icon is visible from build success or `NotifyIcon.Visible`. Run `git diff --check`, then commit only the two project settings, friend declaration, host, factory, cleanup helper, icon, and the two test files as `feat: add WinForms tray icon host`.
## Task 4: Connect testable actions and the service to WPF startup/shutdown

**Files:**
- Create: `src/DesktopPuppy/Pet/Tray/ITrayPetWindow.cs`
- Create: `src/DesktopPuppy/Pet/Tray/TrayApplicationActions.cs`
- Create: `tests/DesktopPuppy.Tests/Pet/Tray/TrayApplicationActionsTests.cs`
- Modify: `src/DesktopPuppy/Pet/PetWindow.xaml.cs` — implement the framework-independent pet-window seam.
- Modify: `src/DesktopPuppy/App.xaml.cs`

**Interfaces:**

```csharp
public interface ITrayPetWindow
{
    bool IsVisible { get; }
    bool IsMinimized { get; }
    void Show();
    void Restore();
    void Hide();
    void Activate();
    void Close();
}

public sealed class TrayApplicationActions : ITrayApplicationActions
{
    public TrayApplicationActions(ITrayPetWindow petWindow, Action openStatistics);
    public void ShowPet();
    public void HidePet();
    public void OpenStatistics();
    public void ExitApplication();
}
```

- [ ] **Step 1: Write five action tests before action or App code.** Every fact gets separate Purpose/Coverage/Target effect source comments:
  - `Show_hidden_pet_shows_and_activates_existing_window`: hidden fake window; effect is one `Show` and one `Activate`, with no `Close`.
  - `Show_visible_normal_pet_does_not_show_again`: visible, non-minimized fake; effect is no `Show`/`Restore` and one `Activate`.
  - `Show_minimized_pet_restores_and_activates_existing_window`: visible minimized fake; effect is one `Restore`, one `Activate`, no new `Show` or `Close`.
  - `Hide_pet_never_closes_application`: fake starts visible; effect is one `Hide`, zero `Close` and zero `Exit` behavior.
  - `Open_statistics_preserves_hidden_pet_and_exit_closes_window`: use two independently arranged fixtures or split this into two facts if either assertion describes multiple behaviors; statistics invokes only the injected callback while hidden; exit calls `Close` on the same existing fake window once. Prefer two facts if that keeps each assertion focused, so the final test count may be six.
  Fake `ITrayPetWindow` records operation counts and updates `IsVisible` only when `Show`/`Hide` runs. No WPF Window, UI automation, desktop input, or database is used.
- [ ] **Step 2: Run focused tests and verify RED.** Run:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
& 'C:\Program Files\dotnet\dotnet.exe' test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~TrayApplicationActionsTests' --verbosity minimal -p:NuGetAudit=false --maxcpucount:1 --nodeReuse:false
```

Expected: compilation fails only because `ITrayPetWindow` and `TrayApplicationActions` do not exist; no unrelated build/test failure is accepted as RED evidence.
- [ ] **Step 3: Implement the framework-independent actions after RED.** `ShowPet` calls `Show()` only if hidden, calls `Restore()` only if minimized, then calls `Activate()`. `HidePet` calls only `Hide()`. `OpenStatistics` calls only the injected open/activate callback and never changes pet visibility. `ExitApplication` calls `Close()` on the existing pet. Rerun the focused tests and confirm GREEN.
- [ ] **Step 4: Make `PetWindow` implement `ITrayPetWindow`.** Existing `Window` members satisfy `IsVisible`, `Show`, `Hide`, `Activate`, and `Close`; add only `IsMinimized => WindowState == WindowState.Minimized` and `Restore() => WindowState = WindowState.Normal`. Keep all current interaction/placement/event behavior unchanged.
- [ ] **Step 5: Wire `App` to the tested action class and real host.** Add one `TrayApplicationActions` field and one `TrayCommandService` field. After successful single-instance acquisition and after constructing the one `PetWindow`, create actions with that same window and the shared statistics open/activate callback; show the pet before calling host Start so initialization failure leaves it visible; create/start exactly one WinForms host in the first process only. On a second-instance activation callback, dispatch to `_trayCommandService.ActivateExistingInstance()` if service exists, otherwise `_trayApplicationActions?.ShowPet()`; never construct another window. Keep actions on the WPF Dispatcher.
- [ ] **Step 6: Reuse statistics and preserve normal shutdown.** Extract the current `PetWindow_OnStatisticsRequested` body to `OpenOrActivateStatisticsWindow()`. Route both the pet event and `OpenStatistics` action to that method; do not show the pet as part of statistics opening. `ExitApplication` closes the main pet window under `ShutdownMode.OnMainWindowClose`. In `OnExit`, detach the pet event, dispose the tray service before the rest of WPF-owned resources, clear tray fields, and preserve current collector/store/guard/placement cleanup.
- [ ] **Step 7: Verify integration.** Run the Task 3 Release app build, the focused `TrayApplicationActionsTests`, `TrayCommandServiceTests`, `TrayContextMenuFactoryTests`, and `TrayResourceCleanupTests` filters. Expected: all pass; no active-desktop UI suite is run. Check the exact startup/single-instance/shutdown call sequence and `git diff --check`.
- [ ] **Step 8: Commit lifecycle integration.** Commit only `App.xaml.cs`, `PetWindow.xaml.cs`, the two action-contract/source files, and `TrayApplicationActionsTests.cs` as `feat: wire tray actions into WPF lifecycle`.
## Task 5: Full regression, status record, and manual handoff

**Files:**
- Modify: `docs/implementation-status.md`

- [ ] **Step 1: Build the full solution in Release.** Run:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
& 'C:\Program Files\dotnet\dotnet.exe' build DesktopPuppy.sln --configuration Release --no-restore --no-incremental --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false -p:NuGetAudit=false
```

Expected: successful serialized solution build; record actual warnings/errors, not an assumed zero.
- [ ] **Step 2: Run the complete deterministic unit suite.** Run:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-build --no-restore --verbosity minimal -p:NuGetAudit=false --maxcpucount:1 --nodeReuse:false
```

Expected: record actual passed/failed/skipped totals. The previous clean baseline in this worktree was 175/175; compare the final count and investigate any regression. Do not run `Category=Ui` or active-desktop click automation as part of this feature.
- [ ] **Step 3: Update `docs/implementation-status.md` with a dated tray milestone.** Record the selected built-in WinForms implementation; changed files; exact build and test command outcomes/counts; and all four manual checks below with separate Purpose, Coverage, Target effect, actual result, evidence/build identity, and current status `Not run — Manual required`. Do not infer a manual pass from source inspection, framework behavior, unit tests, or a successful build.
- [ ] **Step 4: Include the operator acceptance checklist verbatim in meaning.** Track each separately:
  - `TRAY-01 — Icon and menu`: **Purpose:** verify real Explorer icon and menu availability/placement. **Coverage:** run Release, inspect normal/overflow tray, right-click, inspect labels/order/placement, dismiss and reopen. **Target effect:** one icon; correctly placed menu with exactly the approved commands; no duplicate icon.
  - `TRAY-02 — Hide, continue, restore, statistics`: **Purpose:** prove Hide is not process shutdown. **Coverage:** hide, verify process remains, open/close statistics while hidden, restore. **Target effect:** tracking/statistics continue while hidden and the same pet returns.
  - `TRAY-03 — Second launch and exit`: **Purpose:** prove single-instance recovery and normal exit. **Coverage:** hide, launch the Release app again, inspect process/window, exit from tray, and separately check the existing close button. **Target effect:** the original pet restores, there is one product process/pet, and exit removes the app/icon.
  - `TRAY-04 — Explorer restart`: **Purpose:** prove framework taskbar recreation behavior on the real machine. **Coverage:** operator restarts Explorer normally while the app is running, waits for the taskbar, then checks the icon/menu. **Target effect:** exactly one icon returns and the menu remains usable without another pet/process. Never automate this check.
- [ ] **Step 5: Review scope and commit status.** Check only intended tray source/tests, application/project files, and status documentation changed; preserve unrelated files. Run `git diff --check`, then commit only `docs/implementation-status.md` as `docs: record WinForms tray implementation status`.
- [ ] **Step 6: Handoff accurately.** Report automated evidence and clearly state TRAY-01–04 still need the user's manual execution. Do not mark them complete until the user supplies results.

## Spec Coverage Self-Review

- Framework choice, no third-party package, WPF dispatcher ownership, no WinForms window/message loop/direct Shell interop: Tasks 1 and 3.
- Menu labels/order and mapping: Task 3; actual Shell placement and visibility: TRAY-01 manual only.
- Temporary original generic multi-resolution icon and ownership/disposal: Task 3.
- Show/hide/statistics/exit behavior and safe initialization failure: Tasks 2 and 4, with fake-window unit coverage in Task 4.
- Single-instance activation and same pet/window: Tasks 2 and 4; real two-process proof: TRAY-03 manual.
- Taskbar/Explorer recovery: framework-provided .NET 10 behavior is not overstated; TRAY-04 manual only.
- Required Purpose/Coverage/Target effect for every test: Tasks 2 and 3.
- Full Release build/unit suite and implementation status: Task 5.
- TRAY-01–04 remain `Not run — Manual required` unless the operator executes them: Task 5.

## Plan Self-Review

- Placeholder scan: no unfinished or vague implementation steps; STA menu tests exercise only managed ContextMenuStrip structure and click mapping; they do not claim Shell coverage.
- Type consistency: `TrayCommandService` consumes `ITrayIconHost` and `ITrayApplicationActions`; the WinForms host emits `TrayIconCommandEventArgs`; `App` composes the tested action class with one existing PetWindow; the menu factory maps only the four enum values.
- Isolation: save and execute this plan in `C:\Users\ton20\.codex\worktrees\wpf-tray-icon\desktop-puppy` on `codex/wpf-tray-icon`; do not edit the dirty primary checkout.
