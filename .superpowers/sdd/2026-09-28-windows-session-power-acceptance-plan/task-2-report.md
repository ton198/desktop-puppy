# Task 2 Report — Windows Session/Power Acceptance

## Scope and starting state

Implemented only Task 2 from `task-2-brief.md` on branch `codex/windows-e2e-acceptance`, starting at `07716fb193cfc6431a06bda138e0259d7e5d7e54`. The pre-existing Task 2 changes were inspected and retained; no existing implementation was replaced. The working tree initially contained exactly the three modified controller files and four new runtime/test files described in the handoff. This report is the only additional file in scope.

Task 2 adds an isolated app and fixture lifecycle, a real Windows session/power event observer, controller event display dispatch, and conservative cleanup. The lock/unlock and suspend/resume scenario methods deliberately return `Incomplete` until Tasks 3/4 add operator actions and persisted-session boundary validation.

## Changed file scope

- `tests/DesktopPuppy.WindowsAcceptance/AcceptanceRun.cs` — creates a GUID-owned temporary run, initializes isolated `usage.db`, launches one product process with only `DESKTOP_PUPPY_USAGE_DB_PATH` overridden, launches two fixture child processes, records their PIDs and fixture Automation IDs, subscribes to the production `SystemEventsSessionPowerEventSource`, and tears down only owned children and the owned run directory.
- `tests/DesktopPuppy.WindowsAcceptance/ForegroundFixtureWindow.xaml` and `.xaml.cs` — stable generic fixture title and run-local UI Automation IDs; fixture B does not activate on startup.
- `tests/DesktopPuppy.WindowsAcceptance/App.xaml.cs` — fixture mode and explicit controller shutdown lifecycle.
- `tests/DesktopPuppy.WindowsAcceptance/WindowsAcceptanceWindow.xaml` and `.xaml.cs` — starts/cancels isolated startup, displays observed event counts on the UI dispatcher, cleans up on completion/cancel/close, and keeps scenario pass impossible at this stage.
- `tests/DesktopPuppy.Tests/WindowsAcceptance/AcceptanceRunTests.cs` — verifies supported event filtering and that recorded lock/unlock notifications without later scenario validation still produce `Incomplete`.
- `.superpowers/sdd/2026-09-28-windows-session-power-acceptance-plan/task-2-report.md` — this evidence report.

No production files, Task 3/4 code, or Task 5 status files were changed.

## TDD evidence

The handoff reports the following prior red/green work: the two observer tests were first run RED because `AcceptanceEventRecorder` did not exist; after its implementation, both passed (2/2). This run did not repeat the historical RED state. Fresh verification below reran the complete current suite, including those tests, and reports 115/115 passing.

The handoff also reports an earlier 115/115 full unit-suite pass. This continuation independently reran it after inspecting the final files and obtained the same result.

## Final verification commands and output

1. Command: `dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --no-restore --verbosity minimal`

   Final output (environment rendered in Chinese):

   ```text
   DesktopPuppy.WindowsAcceptance.Core -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\tests\DesktopPuppy.WindowsAcceptance.Core\bin\Debug\net10.0\DesktopPuppy.WindowsAcceptance.Core.dll
   DesktopPuppy -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\src\DesktopPuppy\bin\Debug\net10.0-windows\DesktopPuppy.dll
   DesktopPuppy.WindowsAcceptance -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\tests\DesktopPuppy.WindowsAcceptance\bin\Debug\net10.0-windows\DesktopPuppy.WindowsAcceptance.dll
   DesktopPuppy.Tests -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\tests\DesktopPuppy.Tests\bin\Debug\net10.0-windows\DesktopPuppy.Tests.dll
   总共 1 个测试文件与指定模式相匹配。
   已通过! - 失败:     0，通过:   115，已跳过:     0，总计:   115，持续时间: 1 s - DesktopPuppy.Tests.dll (net10.0)
   ```

2. Command: `dotnet build DesktopPuppy.sln --configuration Release --no-restore --verbosity minimal`

   Final result:

   ```text
   DesktopPuppy.WindowsAcceptance.Core -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\tests\DesktopPuppy.WindowsAcceptance.Core\bin\Release\net10.0\DesktopPuppy.WindowsAcceptance.Core.dll
   DesktopPuppy -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\src\DesktopPuppy\bin\Release\net10.0-windows\DesktopPuppy.dll
   DesktopPuppy.WindowsAcceptance -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\tests\DesktopPuppy.WindowsAcceptance\bin\Release\net10.0-windows\DesktopPuppy.WindowsAcceptance.dll
   DesktopPuppy.Tests -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\tests\DesktopPuppy.Tests\bin\Release\net10.0-windows\DesktopPuppy.Tests.dll
   已成功生成。
       0 个警告
       0 个错误
   已用时间 00:00:07.99
   ```

3. `git diff --check` completed without whitespace errors. Git printed only line-ending normalization warnings for the three modified XAML/C# controller files (LF will be replaced by CRLF on a later Git write).

4. Safety/process check: the shell environment had no `DESKTOP_PUPPY_WINDOWS_ACCEPTANCE` or `DESKTOP_PUPPY_EXE` value, and a name-based process query at the end found no `DesktopPuppy` or `DesktopPuppy.WindowsAcceptance` process. An earlier process listing showed a group of idle `dotnet` processes; their CPU times stayed unchanged during a 2-second observation, and no `testhost`/`vstest.console` process appeared. A WMI command-line inventory was denied by Windows access control, so process command lines were not independently confirmed. No existing process was terminated.

## Safety and cleanup limits

- The runtime checks opt-in, a local interactive session, a fully qualified existing `DesktopPuppy.exe`, and the absence of any already-running process named `DesktopPuppy` before creating the run directory. It repeats this preflight immediately before allocation. An existing product process causes `Skipped` before temporary DB creation and is not activated or modified.
- The product is launched exactly once for a run; only its isolated DB-path environment variable is overridden. Fixtures are children of the acceptance controller and the run object retains only its owned child `Process` instances/PIDs and fixture IDs for cleanup/selection.
- The recorded observer payload consists only of event kind and timestamp. Fixture windows use a generic title; run-specific IDs are assigned to UI Automation metadata and are not added to observer/result logs.
- Disposal unsubscribes the event observer, asks owned children to close, waits up to five seconds per child and then kills/waits only that owned child if necessary, and deletes only the scoped GUID directory through `AcceptanceOwnedDirectory.DeleteOwned()`. If child cleanup fails, directory deletion is not attempted, so potentially useful state is retained rather than recursively deleting while a child might still be using it. The initialized store handle is disposed before product launch; the product is stopped before the run directory is removed.
- The prior implementer reports an opt-out utility launch visibly showed `Skipped`, with no new temp run directory or product process reported, and the controller was closed. This continuation did not repeat that GUI launch or independently confirm the process inventory; WMI command-line inspection was denied as noted above.
- No opt-in child lifecycle was run. No lock, suspend, sleep, wake, or unlock action was automated or performed. The real event source is wired in code, but event receipt from a live transition is unverified. Persisted session edges are not queried or checked in Task 2. No claim of real Windows acceptance is made.
- Consequently, `RunLockUnlockAsync` and `RunSuspendResumeAsync` remain explicitly non-passing and return `Incomplete`. A future task must add visible operator steps plus real-event and persisted-edge validation before either can return `Passed`.
