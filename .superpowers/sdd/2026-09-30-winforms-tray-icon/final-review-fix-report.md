# Final-review fixes — 2026-09-30

**Worktree:** `C:\Users\ton20\.codex\worktrees\wpf-tray-icon\desktop-puppy`
**Branch:** `codex/wpf-tray-icon`
**Base:** `9a3fb6d16bef3b69b8f7e5ca7df405c6969d69ed`
**Source/tests commit:** `9bc7f7ea3ce6dfed56b77f49ee5b1633ebfd562b`
**SDK:** .NET `10.0.401`

## Changes

- Replaced the fake `LastShowTarget` self-identity assertion and property. `Second_instance_activation_routes_twice_when_tray_start_fails` now proves only two routes through the injected `ITrayApplicationActions`, one attempted host start, and continued tray unavailability. It does not claim WPF window identity.
- Added a minimal shared `TrayDiagnostics` helper that attempts fixed Console and Debug diagnostics independently and catches writer/listener failures. `TrayCommandService.Start`, `TrayResourceCleanup.Run`, and the tray-disposal catch in `App.OnExit` now use safe, fixed diagnostic text without exception details.
- Added `Cleanup_continues_when_diagnostic_sink_throws`, with separate Purpose/Coverage/Target effect comments. It injects a throwing sink and verifies the later cleanup action still runs.

## TDD evidence

The new cleanup regression test was added before the production overload. The focused RED command was:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
& 'C:\Program Files\dotnet\dotnet.exe' test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~TrayCommandServiceTests|FullyQualifiedName~TrayResourceCleanupTests' --verbosity minimal -p:NuGetAudit=false --maxcpucount:1 --nodeReuse:false
```

**RED exit code 1**, expected compile error at the injected-sink call because the overload did not yet exist (`CS1593: Action does not take 1 argument`). After adding the safe overload/helper, the focused tray suite passed as recorded below.

## Fresh verification on source commit

### Focused tray tests

Command:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
& 'C:\Program Files\dotnet\dotnet.exe' test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~DesktopPuppy.Tests.Pet.Tray' --verbosity minimal -p:NuGetAudit=false --maxcpucount:1 --nodeReuse:false
```

**Exit code 0.** One test file matched; **18 passed, 0 failed, 0 skipped; 18 total**.

### Serialized Release solution build

Command:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
& 'C:\Program Files\dotnet\dotnet.exe' build DesktopPuppy.sln --configuration Release --no-restore --no-incremental --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false -p:NuGetAudit=false
```

**Exit code 0.** Build succeeded; **0 errors, 26 warnings**. Warning breakdown:

- **24 `NU1801`**: configured local source `C:\Program Files (x86)\Microsoft SDKs\NuGetPackages\` is missing; 12 warnings on `DesktopPuppy.csproj`, 12 on generated `DesktopPuppy_s23ts5db_wpftmp.csproj`.
- **2 `WFO0003`**: existing high-DPI manifest configuration, once on `DesktopPuppy.csproj` and once on `DesktopPuppy_s23ts5db_wpftmp.csproj`.

The warning counts are retained as observed; no warning suppression was introduced.

### Full deterministic test suite

Command, run after the solution build:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
& 'C:\Program Files\dotnet\dotnet.exe' test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-build --no-restore --verbosity minimal -p:NuGetAudit=false --maxcpucount:1 --nodeReuse:false
```

**Exit code 0.** **193 passed, 0 failed, 0 skipped; 193 total** (3 seconds).

## Manual checks and exclusions

`Category=Ui`, the UI-test project, app launch, Explorer automation/restart, and all TRAY manual acceptance were not run. **TRAY-01–04 remain `Not run — Manual required`.** The second-instance unit test does not establish WPF window identity; TRAY-03 remains the real manual check. No merge or worktree cleanup was performed.

## Diff check

`git diff --check` and `git diff --cached --check` both returned exit code 0. Git emitted LF-to-CRLF conversion notices for the new report and the pre-existing, separately modified `task-3-report.md`; no whitespace errors were reported.