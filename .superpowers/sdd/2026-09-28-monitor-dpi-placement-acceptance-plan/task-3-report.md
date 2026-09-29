# Task 3 report — Placement service and deterministic behavior

## Scope and base

Implemented only Task 3 from the monitor/DPI placement acceptance plan in `C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy`, branch `codex/windows-e2e-acceptance`, starting at `814c8cb3f000b39c0e66720b3a81dac0fa2c38ff`.

The service consumes the Task 1/2 monitor and native window interfaces. It does not instantiate or move WPF windows itself, subscribe to or dispatch display-change callbacks, modify App/PetWindow/manifest files, persist settings, enumerate/change hardware in tests, or add acceptance scenarios.

## TDD evidence

### RED — service tests first

Added the six requested fake-backed service Facts with individual Purpose/Coverage/Target effect comments before adding production service code. Command:

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --filter "FullyQualifiedName~PetWindowPlacementServiceTests" -m:1 /nodeReuse:false
```

Observed result: exit code 1; compilation failed at the six service construction sites with `CS0246` because `PetWindowPlacementService` did not yet exist. The only warning was the fake provider's not-yet-raised event (`CS0067`). This was the expected missing implementation RED; no tests were run.

After adding policy coverage for the specified physical-pixel conversion and rounding behavior, the combined RED command was:

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --filter "FullyQualifiedName~PetWindowPlacementServiceTests|FullyQualifiedName~PetPlacementPolicyTests|FullyQualifiedName~DpiGeometryPolicyTests" -m:1 /nodeReuse:false
```

Observed result: exit code 1; expected missing service `CS0246` and missing `DpiGeometryPolicy.ToPhysicalExtent` / `RoundPhysicalCoordinate` `CS0117` errors. No tests were run.

The strengthened `Forget_discards_cached_state_when_pet_window_closes` regression also had its own RED before adding the `Forget(hwnd)` call between simulated lifetimes:

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --filter "FullyQualifiedName~Forget_discards_cached_state_when_pet_window_closes" -m:1 /nodeReuse:false
```

Observed result: 1 failed, 0 passed. Expected bounds were `(700, 500, 960, 710)`; stale cached removed-monitor placement produced `(-240, 100, 20, 310)`. After adding `Forget(hwnd)`, the same command produced 1 passed, 0 failed.

### GREEN — final verification

Focused placement and policy tests:

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --filter "FullyQualifiedName~PetWindowPlacementServiceTests|FullyQualifiedName~PetPlacementPolicyTests|FullyQualifiedName~DpiGeometryPolicyTests" -m:1 /nodeReuse:false
```

Exact test summary: `已通过! - 失败: 0，通过: 26，已跳过: 0，总计: 26` (exit code 0).

Previously reviewed native geometry and adapter contracts:

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --filter "FullyQualifiedName~MonitorDisplayContractTests|FullyQualifiedName~Win32MonitorDisplayProviderTests|FullyQualifiedName~Win32WindowPositionerTests" -m:1 /nodeReuse:false
```

Exact test summary: `已通过! - 失败: 0，通过: 10，已跳过: 0，总计: 10` (exit code 0).

Complete unit test project:

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release -m:1 /nodeReuse:false
```

Exact test summary: `已通过! - 失败: 0，通过: 146，已跳过: 0，总计: 146` (exit code 0).

Application Release build:

```text
dotnet build src/DesktopPuppy/DesktopPuppy.csproj --configuration Release -m:1 /nodeReuse:false
```

Exact result: `已成功生成。 0 个警告 0 个错误` (exit code 0).

`git diff --check` completed without whitespace errors. Git printed only its configured LF-to-CRLF conversion notices for the four modified tracked text files.

## Implementation behavior

- Converts requested DIP width/height through the live HWND DPI (`dpi / 96.0`) and ceilings each physical extent. Physical coordinates use `MidpointRounding.AwayFromZero`.
- Adapts physical monitor work areas and HWND positions to the existing pure `MonitorWorkArea` / `PetPosition` policy types at the service boundary. The policy surfaces now document their physical-pixel unit contract; the 20-pixel margin remains in physical pixels.
- Clamps to the policy's 20-physical-pixel visible edge and calls native `SetBounds` only when the final integer `PhysicalRect` differs from actual HWND bounds.
- Keeps each HWND's last physical request by monitor ID. If its last monitor is no longer enumerated, it uses the retained request's position, selects the current primary through `PetPlacementPolicy.ResolveMonitor`, recomputes extents at current HWND DPI, and clamps on that primary; the OS-relocated current bounds do not replace the removed-monitor request.
- `Forget(hwnd)` clears that window's cached monitor/position state; `Dispose()` clears service state. The service has no WPF dependency and performs no dispatcher work.
- All new service tests use fake providers and fake window positioners, including negative monitor coordinates; no real WPF window or display setting is touched.

## Touched files

- `src/DesktopPuppy/Pet/Placement/PetWindowPlacementService.cs` (created)
- `src/DesktopPuppy/Pet/Placement/PetPlacementPolicy.cs` (modified)
- `src/DesktopPuppy/Pet/Placement/DpiGeometryPolicy.cs` (modified)
- `tests/DesktopPuppy.Tests/Pet/PetWindowPlacementServiceTests.cs` (created)
- `tests/DesktopPuppy.Tests/PetPlacementPolicyTests.cs` (modified)
- `tests/DesktopPuppy.Tests/Pet/DpiGeometryPolicyTests.cs` (modified)
- `.superpowers/sdd/2026-09-28-monitor-dpi-placement-acceptance-plan/task-3-report.md` (created)

## Concerns

No unresolved spec/API ambiguity or implementation blocker. Hardware acceptance and Task 4 dispatcher/App/PetWindow wiring remain intentionally out of scope.
