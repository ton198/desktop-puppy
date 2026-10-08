# Windows acceptance checklist — 2026-10-01

This is the next stage after the statistics-correctness fixes. Do not run system transitions automatically. Save unrelated work first; do not test against an already-running product instance or the user's ordinary database. Record only anonymized application aliases/durations and scenario outcomes, not private window-title text.

## Prepare an isolated manual run

From the project checkout in PowerShell, first inspect existing processes. If one is running, stop and let the operator decide when to exit it normally; never terminate it automatically.

```powershell
Get-Process DesktopPuppy -ErrorAction SilentlyContinue
$productExe = 'C:\Users\ton20\Documents\works\projects\desktop-puppy\src\DesktopPuppy\bin\Release\net10.0-windows\DesktopPuppy.exe'
$runDirectory = Join-Path (Get-Location) ('.local\acceptance\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$env:DESKTOP_PUPPY_USAGE_DB_PATH = Join-Path $runDirectory 'usage.db'
& $productExe
```

Keep the same shell/environment for the second launch. Data stays in this private run directory; do not delete it until the test app is confirmed exited. Clearing the environment override afterward does not delete the data.

## Ordered scenarios

| ID | Operator steps | Pass requires | Recorded status |
|---|---|---|---|
| USAGE-01 | Start while an ordinary application is foreground; inspect statistics after returning to it for at least one minute. | The initial foreground application is counted without needing an unrelated application switch. | Not run |
| USAGE-02 | Keep the same browser/editor window foreground; change its tab/file title; keep each chosen title for more than two seconds. Inspect title detail afterward. | Both stable titles are represented; a brief title abandoned before two seconds is not recorded. Sampling can miss transitions shorter than its interval. | Not run |
| TRAY-01 | Inspect normal/overflow tray; right-click, dismiss, reopen. | Exactly one icon; menu contains Show Desktop Puppy, Hide Desktop Puppy, Usage Statistics, AI 设置, separator, Exit Desktop Puppy; menu placement is usable. | Not run |
| TRAY-02 | Hide through tray; use an ordinary application for at least one minute; open statistics from tray, close statistics, then show pet. | Product remains running; tracking continues while hidden; the original pet returns. | Not run |
| TRAY-03 | Hide pet; launch the same executable again from the same shell; then exit from tray. Separately launch and exit using the pet close button. | Second launch restores the original pet, with one persistent product process; each exit path removes app and tray icon. | Not run |
| TRAY-04 | Only if the operator is ready, manually restart Explorer and wait for the taskbar. | Exactly one icon returns, menu works, no duplicate pet/process. | Not run |
| E2E-LOCK-01 | Use the existing opt-in acceptance controller and manually lock/unlock as instructed. | Ordered real notifications and isolated persisted session boundaries satisfy controller checks. | Not run |
| E2E-POWER-02 | Use the controller and manually suspend/resume, unlocking if needed. | Ordered real Suspend/Resume notifications and isolated session boundaries satisfy controller checks. Does not distinguish sleep from hibernate. | Not run |
| E2E-DISPLAY-01 | On two real displays with different measured DPI, manually move the same pet and confirm captures in the controller. | Same HWND/PID; measured DPI-scaled geometry and edge-clamping checks pass on both displays. | Not run |
| E2E-DISPLAY-02 | Use the controller; manually remove the selected display only when ready. | Same pet recovers into a remaining display with valid placement. | Not run |

Do not automate forced termination of the normal application to test recovery. Automated SQLite/debounce recovery tests use private fixtures and do not require a real crash.

## Lock/power/display controller

After exiting the manual tray-test app, follow `tests/DesktopPuppy.WindowsAcceptance/README.md`. The controller owns its own isolated product/database. Supply the absolute executable above and opt in explicitly:

```powershell
$env:DESKTOP_PUPPY_EXE = $productExe
$env:DESKTOP_PUPPY_WINDOWS_ACCEPTANCE = '1'
# Only enable this additional flag when ready for real display hardware tests:
# $env:DESKTOP_PUPPY_DISPLAY_ACCEPTANCE = '1'
& 'C:\Users\ton20\Documents\works\projects\desktop-puppy\tests\DesktopPuppy.WindowsAcceptance\bin\Release\net10.0-windows\DesktopPuppy.WindowsAcceptance.exe'
```

The controller never initiates lock, sleep, display removal, or power setting changes. Missing prerequisites are Skipped, not Passed. Cancellation or timeout is Incomplete. Only observed evidence can be Passed/Failed.

## Result record

For each scenario record: tested build/source identity, local execution time, outcome (Not run / Skipped / Incomplete / Failed / Passed), anonymized observation, and any required next action. Keep automated UI results separate from manual Shell/OS/hardware results. Full input-injecting UI regression should run only after the operator is ready to stop using the desktop.
