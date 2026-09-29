# Task 5 report — Opt-in mixed-DPI live acceptance

## Scope and status

Implemented Task 5 only in `C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy`, branch `codex/windows-e2e-acceptance`, based on `d0798da2ab04e77c1ce03b79e96512765de9604a`. No Task 6/7 work was performed. The Task 5 report, acceptance README, visible controller, isolated run lifecycle, scenario, and fake-backed deterministic tests are included in the Task 5 change set.

The standalone acceptance controller was built but **not launched**. No display setting, monitor topology, or window position was changed by the acceptance utility. The E2E-DISPLAY-01 hardware scenario is **Not run**. `Category=Ui` was **Not run** because existing interaction UI tests inject `SendInput` into the live desktop.

## Implementation and safety flow

- The visible **Run mixed-DPI display acceptance** selection describes Purpose, Coverage, and Target effect. It instructs the operator to move the same pet to each requested display and confirm each read-only capture manually.
- E2E-DISPLAY-01 requires both `DESKTOP_PUPPY_WINDOWS_ACCEPTANCE=1` and `DESKTOP_PUPPY_DISPLAY_ACCEPTANCE=1`, a local interactive non-remote Windows session, at least two enumerated monitors, a valid absolute `DESKTOP_PUPPY_EXE`, and no already-running `DesktopPuppy` process. A failed gate produces **Skipped** before the acceptance child app or owned run database is started/created. With a missing opt-in or failed session gate, monitor enumeration is not attempted. The display count is checked before the product/process preflight is repeated immediately before allocating the run directory.
- `ChooseDisplay` creates the read-only display scenario only after the operator selects it. Any `Skipped` result at selection disposes that scenario/provider. The same preflight is repeated before startup and again within `DisplayAcceptanceRun.StartAsync`; only after those checks does it create the owned temporary SQLite directory, initialize the unique database, and start the isolated app with that DB path.
- UI Automation searches by the isolated process PID and exact `PetWindow` AutomationId. The scenario rechecks UIA AutomationId and process PID before returning a handle and before every observation. The controller retains that one HWND for both operator-confirmed captures; validation rejects changed HWND, PID, target-display membership, or monitor mapping. There is no `EnumWindows` early-stop path in the display scenario.
- Actual monitor enumeration and HWND geometry/DPI observation use the reviewed `Win32MonitorDisplayProvider` and `Win32WindowPositioner`. Acceptance code contains no monitor/HWND/DPI P/Invoke. `GetDpiForWindow`, physical bounds, containing monitor, monitor bounds, and work area are read-only observations. The acceptance flow does not call `SetWindowPos`, does not change display settings, and does not use screenshots.
- With equal nonzero measured DPI on both requested displays and the same HWND, validation always returns **Skipped** and includes both run-local display aliases and the measured DPI values before evaluating mixed-DPI size/clamp assertions. With distinct DPI, it validates live process observations, expected DPI-scaled physical size, target display, production `PetPlacementPolicy` clamping, and at least 20 physical pixels visible in both dimensions.
- Cleanup is shared by startup failure and `DisposeAsync`. The owned DB directory is deleted only when the stop operation returns affirmative process-exited confirmation. A stop exception or unconfirmed exit retains the private directory. Scenario/provider disposal is in an independent `finally`, so it still runs if stopping fails. `ChooseDisplay` skip also disposes the just-created provider. The fake-backed cleanup test covers stop failure, unconfirmed exit, confirmed exit, and unconditional scenario disposal.

## TDD evidence — cleanup failure path

The regression test was already present before implementing its cleanup seam. The first focused build attempt could not compile because `DisplayAcceptanceRunCleanup` had not yet been introduced; that compile error was not counted as behavioral RED. After adding the seam with the unsafe deletion behavior corresponding to the defect, the focused test failed on the expected directory-retention assertion.

### RED

Command:

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --no-restore --filter FullyQualifiedName~Failed_display_child_cleanup_retains_run_directory_and_disposes_provider --verbosity minimal
```

Relevant exact output:

```text
[xUnit.net 00:00:04.21]     DesktopPuppy.Tests.WindowsAcceptance.DisplayAcceptanceScenarioTests.Failed_display_child_cleanup_retains_run_directory_and_disposes_provider [FAIL]
  失败 DesktopPuppy.Tests.WindowsAcceptance.DisplayAcceptanceScenarioTests.Failed_display_child_cleanup_retains_run_directory_and_disposes_provider [1 s]
  错误消息:
   Assert.False() Failure
Expected: False
Actual:   True
  堆栈跟踪:
     at DesktopPuppy.Tests.WindowsAcceptance.DisplayAcceptanceScenarioTests.Failed_display_child_cleanup_retains_run_directory_and_disposes_provider() in C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\tests\DesktopPuppy.Tests\WindowsAcceptance\DisplayAcceptanceScenarioTests.cs:line 194
--- End of stack trace from previous location ---

失败!  - 失败:     1，通过:     0，已跳过:     0，总计:     1，持续时间: 1 s - DesktopPuppy.Tests.dll (net10.0)
```

Exit code: `1`. The test observed deletion after the simulated stop failure, reproducing the defect.

### GREEN

After the fix, the helper deletes only on a positive exit result, otherwise throws while retaining the directory, and always invokes scenario/provider disposal in `finally`. The production stop routine treats an invalid/unverifiable process handle as unconfirmed rather than proof of exit.

Command:

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore -m:1 /nodeReuse:false --filter FullyQualifiedName~Failed_display_child_cleanup_retains_run_directory_and_disposes_provider --verbosity minimal
```

Exact final output:

```text
  DesktopPuppy -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\src\DesktopPuppy\bin\Release\net10.0-windows\DesktopPuppy.dll
  DesktopPuppy.WindowsAcceptance.Core -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\tests\DesktopPuppy.WindowsAcceptance.Core\bin\Release\net10.0\DesktopPuppy.WindowsAcceptance.Core.dll
  DesktopPuppy.WindowsAcceptance -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\tests\DesktopPuppy.WindowsAcceptance\bin\Release\net10.0-windows\DesktopPuppy.WindowsAcceptance.dll
  DesktopPuppy.Tests -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\tests\DesktopPuppy.Tests\bin\Release\net10.0-windows\DesktopPuppy.Tests.dll
C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\tests\DesktopPuppy.Tests\bin\Release\net10.0-windows\DesktopPuppy.Tests.dll (.NETCoreApp,Version=v10.0)的测试运行
总共 1 个测试文件与指定模式相匹配。

已通过! - 失败:     0，通过:     1，已跳过:     0，总计:     1，持续时间: 341 ms - DesktopPuppy.Tests.dll (net10.0)
```

Exit code: `0`.

## Build and deterministic test evidence

### Release product app build

Command:

```text
dotnet build src/DesktopPuppy/DesktopPuppy.csproj --configuration Release --no-restore -m:1 /nodeReuse:false
```

Exact output:

```text
  DesktopPuppy -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\src\DesktopPuppy\bin\Release\net10.0-windows\DesktopPuppy.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:01.61
```

Exit code: `0`.

### Release standalone acceptance utility build (not launched)

Command:

```text
dotnet build tests/DesktopPuppy.WindowsAcceptance/DesktopPuppy.WindowsAcceptance.csproj --configuration Release --no-restore -m:1 /nodeReuse:false
```

Exact output:

```text
  DesktopPuppy -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\src\DesktopPuppy\bin\Release\net10.0-windows\DesktopPuppy.dll
  DesktopPuppy.WindowsAcceptance.Core -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\tests\DesktopPuppy.WindowsAcceptance.Core\bin\Release\net10.0\DesktopPuppy.WindowsAcceptance.Core.dll
  DesktopPuppy.WindowsAcceptance -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\tests\DesktopPuppy.WindowsAcceptance\bin\Release\net10.0-windows\DesktopPuppy.WindowsAcceptance.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:04.00
```

Exit code: `0`.

### Full deterministic xUnit project

Command:

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore -m:1 /nodeReuse:false --verbosity minimal
```

Exact final output:

```text
  DesktopPuppy -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\src\DesktopPuppy\bin\Release\net10.0-windows\DesktopPuppy.dll
  DesktopPuppy.WindowsAcceptance.Core -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\tests\DesktopPuppy.WindowsAcceptance.Core\bin\Release\net10.0\DesktopPuppy.WindowsAcceptance.Core.dll
  DesktopPuppy.WindowsAcceptance -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\tests\DesktopPuppy.WindowsAcceptance\bin\Release\net10.0-windows\DesktopPuppy.WindowsAcceptance.dll
  DesktopPuppy.Tests -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\tests\DesktopPuppy.Tests\bin\Release\net10.0-windows\DesktopPuppy.Tests.dll
C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\tests\DesktopPuppy.Tests\bin\Release\net10.0-windows\DesktopPuppy.Tests.dll (.NETCoreApp,Version=v10.0)的测试运行
总共 1 个测试文件与指定模式相匹配。

已通过! - 失败:     0，通过:   162，已跳过:     0，总计:   162，持续时间: 1 s - DesktopPuppy.Tests.dll (net10.0)
```

Exit code: `0`. No `Category=Ui` filter or UI interaction test was run.

## Privacy boundary

User-visible observations/results contain only run-local aliases (`Display 1`, `Display 2`), measured DPI, physical bounds, work areas, and status/reason. Device IDs remain internal to the Native adapter/scenario mapping. The utility does not print raw device IDs, HWND values, unrelated window titles, database rows, or user activity. It does not use screenshot pixels. Native/P/Invoke declarations for monitor enumeration and HWND/DPI calls remain in `src/DesktopPuppy/Native`; the acceptance utility reuses `Win32MonitorDisplayProvider` and `Win32WindowPositioner` and calls only their read methods.

## Not-run boundary and concerns

- **E2E-DISPLAY-01 real hardware:** **Not run**. No manual drags, actual display DPI pair, or live two-display observations are claimed. Fake-backed tests verify deterministic flow/validation only.
- **Standalone acceptance executable:** built only; **not launched**.
- **`Category=Ui`:** **Not run** to avoid live-desktop `SendInput` injection.
- No display topology, resolution, scaling, or system setting was changed.
- Remaining concern: actual UI Automation availability, real mixed-DPI WPF reconciliation, physical coordinates, and hardware-specific behavior require the operator-driven acceptance on a qualifying local setup. If cleanup cannot affirm child exit, the private run directory is intentionally retained for safety.

## Changed files

- `tests/DesktopPuppy.WindowsAcceptance/DisplayAcceptanceScenario.cs` (created)
- `tests/DesktopPuppy.WindowsAcceptance/DisplayAcceptanceRunCleanup.cs` (created; fakeable cleanup contract for Task 5 cleanup failure regression)
- `tests/DesktopPuppy.WindowsAcceptance/AcceptanceRun.cs`
- `tests/DesktopPuppy.WindowsAcceptance/WindowsAcceptanceWindow.xaml`
- `tests/DesktopPuppy.WindowsAcceptance/WindowsAcceptanceWindow.xaml.cs`
- `tests/DesktopPuppy.WindowsAcceptance/README.md`
- `tests/DesktopPuppy.Tests/WindowsAcceptance/DisplayAcceptanceScenarioTests.cs` (created; each added test documents Purpose/Coverage/Target effect)
- `.superpowers/sdd/2026-09-28-monitor-dpi-placement-acceptance-plan/task-5-report.md` (this report)
