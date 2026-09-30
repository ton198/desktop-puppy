# WPF System Tray Integration Design

**Date:** 2026-09-29
**Status:** User-approved approach; awaiting written-spec review
**Scope:** Add a WPF-native tray entry point for the existing Desktop Puppy process

## 1. Goal

Let a user hide and restore the pet without stopping the application, open usage statistics from the tray, and explicitly exit the application. Keep the existing single-instance behavior: a second launch must restore the existing hidden pet rather than create a second pet process.

This is one scoped product subsystem. It does not implement settings, startup registration, activity pause/scheduling, animation assets, or other tray commands.

## 2. Selected approach

Use the community WPF tray library `Hardcodet.NotifyIcon.Wpf` and its `TaskbarIcon` control with a WPF `ContextMenu`. Do not implement `Shell_NotifyIcon` directly and do not add a WinForms tray menu in this step.

Before adding a package reference, verify from authoritative package/repository metadata:

- current stable package identity and version;
- license compatibility with this personal project;
- compatibility with the project's `net10.0-windows` WPF target;
- icon/context-menu API and disposal behavior;
- whether the library restores/recreates the icon after Explorer restarts.

If a material compatibility, maintenance, or license issue is found, stop and report it rather than silently changing to another tray implementation.

The initial icon is a temporary generic, original vector mark embedded in the project. It must not use Snoopy or other unlicensed character art. The later art-creation milestone may replace it.

## 3. User-visible behavior

The tray context menu contains, in this order:

1. **Show Desktop Puppy** — show the existing pet window, restore it from minimized state, and bring it forward; idempotent if already visible and active.
2. **Hide Desktop Puppy** — hide the existing pet window without closing it or shutting down the process; idempotent if already hidden.
3. **Usage Statistics** — open or activate the existing statistics window. If the pet is hidden, keep it hidden.
4. Separator.
5. **Exit Desktop Puppy** — close the pet and shut down the process through the normal WPF exit path.

No extra left-click, double-click, balloon notification, or context-menu behavior is introduced beyond the library's normal display of the WPF context menu. The existing close button on the pet remains a true application exit.

If tray initialization fails, the pet remains visible and the app must not offer a hide action that would leave the user without a way to restore it. Log only a safe, non-sensitive diagnostic; do not include titles, usage data, or credentials.

## 4. Lifecycle and component boundaries

- `App` owns the tray control/service from successful single-instance acquisition until application exit.
- Create and dispose the tray control on the WPF dispatcher. Dispose it during application shutdown so the Explorer icon is removed and callbacks are detached.
- Keep `ShutdownMode.OnMainWindowClose`. Hiding the pet uses `Hide()`, never `Close()`, so usage collection and the statistics window continue to run. The existing pet close button and tray Exit command close the main pet window and preserve the current shutdown semantics.
- Route tray commands through a small application-level command/action boundary. The tray UI must not contain database, usage collection, or Win32 monitor logic.
- Reuse the existing statistics-window open/activate path; opening statistics from the tray must not implicitly show the pet.
- Reuse the existing `SingleInstanceGuard` activation callback. When the existing pet is hidden or minimized, activation restores and shows it, then brings it forward. Do not add a second tray icon or a second pet window for a second launch.
- The tray library integration remains behind a narrow seam so command behavior can be unit-tested without creating Shell icons in the unit-test process.

## 5. Error handling and safety

- Tray construction or icon loading failure is non-fatal to application startup, but disables Hide and records a safe diagnostic while the pet stays visible.
- A command issued during shutdown is ignored safely; repeated show/hide and dispose calls are idempotent.
- Closing the statistics window does not exit the application. Exiting from the tray closes all app windows through the normal WPF shutdown lifecycle and disposes the tray object exactly once.
- No automated test in this step sends keyboard/mouse input to the active desktop, locks the workstation, changes display topology, or touches the production usage database.

## 6. Automated test plan

### 6.1 Unit tests

Add focused tests for the tray command/action boundary. Each test must document **Purpose**, **Coverage**, and **Target effect** in the test source.

- Show command calls the pet show action once and is safe when already visible.
- Hide command calls Hide without requesting application exit; repeated Hide is harmless.
- Statistics command opens/activates statistics and does not show a hidden pet.
- Exit command requests the normal app-exit path exactly once.
- Tray initialization failure leaves the pet visible and Hide unavailable while other available actions remain safe.
- Single-instance activation routes to restore/show/activate even when the pet was hidden; repeated activation does not create another window.
- Disposal is idempotent and detaches tray event handlers.

Tests use fakes for tray creation and pet/application actions; they do not instantiate the real notification-area icon.

### 6.2 UI/integration boundary

Do not claim the actual Shell icon or context menu is covered by the fake-backed unit suite. If a safe, isolated UI Automation path can discover and invoke the tray context menu without `SendInput`, add a separately categorized opt-in UI integration test. Otherwise keep actual tray interaction in the operator acceptance below and state that boundary explicitly. Do not weaken the production UI or add a test-only production command merely to automate clicking the notification area.

## 7. Operator acceptance — manual required

Mark each item **Not run** until the operator actually performs it. The acceptance checklist must include Purpose, Coverage, and Target effect for every case.

### TRAY-01 — Icon and menu visibility (**Manual required**)
- **Purpose:** Confirm the real Shell notification-area icon and WPF menu appear in an interactive Windows session.
- **Coverage:** Start Release app, inspect notification area, open the context menu.
- **Target effect:** One identifiable Desktop Puppy icon and the specified menu commands are visible; no second icon is created by reopening the menu.

### TRAY-02 — Hide, continue running, restore, and statistics (**Manual required**)
- **Purpose:** Confirm hiding is not application shutdown and tray commands remain useful while the pet is hidden.
- **Coverage:** Hide pet; verify it is not visible while the process remains running; open Statistics while pet remains hidden; close statistics; show pet again.
- **Target effect:** Statistics/collection remain available while hidden, the pet returns from the tray, and no duplicate pet window appears.

### TRAY-03 — Second-instance activation and exit (**Manual required**)
- **Purpose:** Confirm single-instance recovery works through the real running application and shutdown cleans up the Shell icon.
- **Coverage:** Hide pet, launch the Release executable a second time, inspect the first app/window/process, then exit through the tray and separately verify the pet close button's established exit behavior.
- **Target effect:** The existing pet becomes visible/active; there is only one product process/pet; tray Exit terminates the app and removes its icon.

Record the actual build identity and exact result for each case. Do not infer a manual pass from unit tests or a successful build.

## 8. Out of scope

- Settings persistence or a settings window.
- Pause/resume behavior before an activity scheduler exists.
- Startup registration, startup delay, or launch-at-login controls.
- AI, daily reports, notifications, balloon tips, or activity scheduling.
- Final character art and animation frames; those belong to the later approved material-creation milestone.
- Real lock/sleep or display-topology acceptance, which remains tracked separately.

## 9. Completion criteria

1. The tray dependency passes the compatibility/license/API review before it is added.
2. Automated command/lifecycle tests cover the listed behaviors and pass.
3. Release app and solution builds pass.
4. Unit tests prove hide does not invoke exit, and second-instance callback restores the existing window.
5. Manual TRAY-01 through TRAY-03 results are visibly tracked as **Not run**, **Incomplete**, **Failed**, or **Passed** with evidence; no unrun item is described as passed.
6. Implementation status records changed files, automated results, and outstanding manual checks.
