# WPF System Tray Integration Design

**Date:** 2026-09-29
**Revised:** 2026-09-30
**Status:** User-approved direction; revised written specification ready for review
**Scope:** Add a Windows notification-area entry point for the existing Desktop Puppy WPF process

## 1. Goal

Let a user hide and restore the pet without stopping the application, open usage statistics from the tray, and explicitly exit the application. Keep the existing single-instance behavior: a second launch must restore the existing hidden pet rather than create a second pet process.

This is one scoped product subsystem. It does not implement settings, startup registration, activity pause/scheduling, animation assets, or other tray commands.

## 2. Selected approach

Use the .NET Windows Forms `System.Windows.Forms.NotifyIcon` component with a Windows Forms `ContextMenuStrip` inside the existing WPF application. This is part of the .NET Windows Desktop framework and adds no third-party NuGet dependency. Do not implement `Shell_NotifyIcon` directly. Set both `<UseWPF>true</UseWPF>` and `<UseWindowsForms>true</UseWindowsForms>` in the existing Windows-targeted project; Microsoft’s Desktop SDK documentation allows either or both UI frameworks in one project.

Keep WPF as the application framework and owner of the process lifecycle. Construct, configure, handle, and dispose the `NotifyIcon`, its `ContextMenuStrip`, and its `System.Drawing.Icon` on the WPF Dispatcher thread. Do not create a WinForms top-level window or call `System.Windows.Forms.Application.Run`; the WPF Dispatcher continues to own the UI message loop. The component’s callbacks route through the testable tray-command seam into the existing WPF application actions.

The tray icon asset is an original, generic multi-resolution `.ico` resource loaded and owned by the tray host. It must not use Snoopy or other unlicensed character art. The later art-creation milestone may replace it.

Authoritative framework references:

- [Microsoft Learn: `NotifyIcon`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon?view=windowsdesktop-10.0)
- [Microsoft Learn: `NotifyIcon.ContextMenuStrip`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon.contextmenustrip?view=windowsdesktop-10.0)
- [Microsoft Learn: `Component.Dispose`](https://learn.microsoft.com/en-us/dotnet/api/system.componentmodel.component.dispose?view=net-10.0)
- [Microsoft Learn: WPF and Windows Forms interoperability](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-and-windows-forms-interoperation)
- [Microsoft Learn: Windows Desktop SDK properties (`UseWPF` / `UseWindowsForms`)](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props-desktop)
- [.NET 10 WinForms `NotifyIcon` source](https://github.com/dotnet/winforms/blob/v10.0.0/src/System.Windows.Forms/System/Windows/Forms/NotifyIcon.cs)

The .NET 10 implementation handles the Windows `TaskbarCreated` message and re-adds the icon after the taskbar/Explorer is recreated. `NotifyIcon.Visible` expresses the component’s requested visibility; it is not a public confirmation that Explorer accepted the icon. Actual Shell presence therefore remains part of manual acceptance.

### Previously considered option — Hardcodet WPF tray library (not selected)

The earlier `Hardcodet.NotifyIcon.Wpf` proposal was stopped at its dependency gate and is not part of this design. Its latest stable version was 2.0.1 (MIT, compatible target framework), but the upstream project had an open, untriaged report of random context-menu placement on a Surface device (#132), an unresolved shutdown-disposal report (#122), and no source/release update since October 2024 at verification time. No package was added. This alternative will not be silently reinstated.

## 3. User-visible behavior

The tray context menu contains, in this order:

1. **Show Desktop Puppy** — show the existing pet window, restore it from minimized state, and bring it forward; safe when already visible.
2. **Hide Desktop Puppy** — hide the existing pet window without closing it or shutting down the process; safe when already hidden.
3. **Usage Statistics** — open or activate the existing statistics window. If the pet is hidden, keep it hidden.
4. Separator.
5. **Exit Desktop Puppy** — close the pet and shut down the process through the normal WPF exit path.

Use the standard right-click notification-area context menu only. No extra left-click, double-click, balloon notification, or other menu behavior is introduced. The existing close button on the pet remains a true application exit.

If managed tray construction, icon loading, or menu initialization fails, startup remains non-fatal: the pet stays visible, the tray service remains unavailable, and the application does not offer Hide from another surface. Log only a safe, non-sensitive diagnostic; do not include titles, usage data, or credentials. Because `NotifyIcon` does not expose a public Shell-registration result, document this limit and verify the real icon/menu manually; do not claim that the `Visible` property alone proves Shell registration.

## 4. Lifecycle and component boundaries

- `App` owns the tray host/service from successful single-instance acquisition until application exit.
- Create, configure, receive events from, and dispose the WinForms tray components on the WPF Dispatcher. Do not run a second WinForms message loop.
- The tray host owns the `NotifyIcon`, `ContextMenuStrip`, generated `ToolStripMenuItem`s, and loaded icon resource. Detach menu/click handlers and clear the component’s context-menu reference before disposing owned objects. Dispose each owned object exactly once; verify the disposal order against .NET 10 behavior during implementation.
- Keep `ShutdownMode.OnMainWindowClose`. Hiding the pet uses `Hide()`, never `Close()`, so usage collection and the statistics window continue to run. The existing pet close button and tray Exit command close the main `PetWindow` and preserve the current shutdown semantics.
- Route tray commands through a small application-level command/action boundary. The tray UI must not contain database, usage collection, or monitor-placement logic.
- Reuse the existing statistics-window open/activate path; opening statistics from the tray must not implicitly show the pet.
- Reuse the existing `SingleInstanceGuard` activation callback. When the existing pet is hidden or minimized, activation restores and shows it, then brings it forward. Do not add a second tray icon or a second pet window for a second launch.
- Keep the WinForms API behind `ITrayIconHost` so command behavior can be unit-tested without creating or displaying a real Shell icon.

## 5. Error handling and safety

- Tray object, menu, or icon-resource initialization failure is non-fatal; leave the pet visible, mark tray actions unavailable, and emit only a fixed safe diagnostic.
- A `NotifyIcon.Visible` value is not treated as proof that the icon is registered by Explorer; TRAY-01 checks the real notification area.
- A command issued during shutdown is ignored safely; repeated show/hide and dispose calls are idempotent.
- Closing the statistics window does not exit the application. Exiting from the tray closes app windows through the normal WPF shutdown lifecycle and disposes tray resources exactly once.
- .NET 10 WinForms source handles the `TaskbarCreated` broadcast by resetting its internal registration state and updating the visible icon. Verify this behavior with the manual Explorer-restart acceptance; do not add duplicate Shell interop.
- No automated test in this step sends input to the active desktop, restarts Explorer, locks the workstation, changes display topology, or touches the production usage database.

## 6. Automated test plan

### 6.1 Unit tests

Add focused tests for the tray command/action boundary. Each test must document **Purpose**, **Coverage**, and **Target effect** separately in the test source.

- Successful host start makes tray actions available.
- Show routes to the existing pet action; app-level show is safe when already visible.
- Repeated Hide requests route only to Hide and never exit the application.
- Statistics opens/activates its window while the pet stays hidden.
- Exit requests the normal application exit path exactly once; later commands are ignored.
- Host initialization failure is non-fatal, leaves the fake pet visible, and blocks tray commands.
- Single-instance activation restores the existing window even if tray setup failed; repeated activation never constructs another window.
- Disposal detaches host events, disposes owned host resources exactly once, and ignores later commands.

Tests use fakes for tray hosting and application actions; they do not instantiate the real notification-area icon, send active-desktop input, or access the production database. If a deterministic STA-only test for menu-item-to-command mapping can be done without registering a Shell icon or displaying UI, it may be added; it must not be presented as real Shell coverage.

### 6.2 UI/integration boundary

Do not claim the actual Shell icon, context-menu location, or Explorer restart behavior is covered by fake-backed unit tests. If a safe, isolated UI Automation path can discover and invoke the tray menu without `SendInput`, add a separately categorized opt-in UI integration test. Otherwise keep these cases in the operator acceptance below. Do not weaken production UI or add a test-only production command merely to automate clicking the notification area.

## 7. Operator acceptance — manual required

Mark each item **Not run** until the operator actually performs it. Every case lists Purpose, Coverage, and Target effect.

### TRAY-01 — Icon and menu (**Manual required**)
- **Purpose:** Confirm the real Windows notification-area icon and `ContextMenuStrip` are available and sensibly positioned.
- **Coverage:** Start the Release app, inspect the notification area/overflow, right-click the icon, inspect labels/order, dismiss and reopen the menu.
- **Target effect:** One identifiable Desktop Puppy icon is present; the menu appears at the pointer near the tray interaction (not at an unrelated/random screen location); exactly the specified commands appear; reopening does not create another icon.

### TRAY-02 — Hide, continue running, restore, and statistics (**Manual required**)
- **Purpose:** Confirm hiding is not application shutdown and tray commands remain useful while the pet is hidden.
- **Coverage:** Hide the pet; verify it is not visible while the process remains running; open Statistics while the pet remains hidden; close Statistics; show the pet again.
- **Target effect:** Statistics/collection remain available while hidden; the existing pet returns from the tray; no duplicate pet window appears.

### TRAY-03 — Second-instance activation and exit (**Manual required**)
- **Purpose:** Confirm single-instance recovery works through the real application and shutdown removes the tray icon.
- **Coverage:** Hide pet; launch the Release executable a second time; inspect the first app/window/process; exit from the tray; separately verify the pet close button's established exit behavior.
- **Target effect:** Existing pet becomes visible/active; there is one product process/pet; exit terminates the app and removes the icon.

### TRAY-04 — Explorer restart recovery (**Manual required**)
- **Purpose:** Confirm the framework recreates the visible icon after Explorer/taskbar restarts.
- **Coverage:** With the Release app running, restart Windows Explorer through a normal operator-controlled method; wait for the taskbar to return; inspect the notification area and open the tray menu.
- **Target effect:** Exactly one icon returns, the menu remains usable, and no second pet/process is created. This check is never run automatically.

Record the actual build identity and exact result for each case. Do not infer a manual pass from unit tests, framework source, or a successful build.

## 8. Out of scope

- Settings persistence or a settings window.
- Pause/resume behavior before an activity scheduler exists.
- Startup registration, startup delay, or launch-at-login controls.
- AI, daily reports, notifications, balloon tips, or activity scheduling.
- Final character art and animation frames; those belong to the later approved material-creation milestone.
- Real lock/sleep or display-topology acceptance, which remains tracked separately.

## 9. Completion criteria

1. The .NET 10 WindowsDesktop project enables WPF and Windows Forms together and builds successfully without a third-party tray package.
2. Automated command/lifecycle tests cover the listed behaviors and pass.
3. Release app and solution builds pass.
4. Unit tests prove Hide does not invoke exit and second-instance activation routes to the existing pet action.
5. Manual TRAY-01 through TRAY-04 results are visibly tracked as **Not run**, **Incomplete**, **Failed**, or **Passed** with evidence; no unrun item is described as passed.
6. Implementation status records changed files, exact automated results, and outstanding manual checks.
