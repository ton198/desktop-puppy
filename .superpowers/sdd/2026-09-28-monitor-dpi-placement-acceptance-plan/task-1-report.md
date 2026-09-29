# Task 1 report — Monitor/DPI geometry contracts

## Scope

Implemented only the Task 1 contract files:

- `src/DesktopPuppy/Native/PhysicalRect.cs`
- `src/DesktopPuppy/Native/MonitorDisplayInfo.cs`
- `src/DesktopPuppy/Native/IMonitorDisplayProvider.cs`
- `src/DesktopPuppy/Native/INativeWindowPositioner.cs`
- `tests/DesktopPuppy.Tests/Native/MonitorDisplayContractTests.cs`

No native monitor APIs, WPF logic, or product placement path were inspected or changed.

## TDD evidence

RED command:

`dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --filter "FullyQualifiedName~MonitorDisplayContractTests" -m:1 /nodeReuse:false`

Result before implementation: build failed with CS0246 because `PhysicalRect` and `MonitorDisplayInfo` did not exist. This was the expected missing-contract failure. The first sandbox attempt to write the test was denied; after approval, the RED run completed. Its unrelated output also reported denied cache writes, but the expected missing-type failures were present.

GREEN command:

`dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --filter "FullyQualifiedName~MonitorDisplayContractTests" -m:1 /nodeReuse:false`

Exact result:

`已通过! - 失败:     0，通过:     3，已跳过:     0，总计:     3，持续时间: 38 ms - DesktopPuppy.Tests.dll (net10.0)`

Exit code: 0.

## Delivered contract behavior

- `PhysicalRect` uses integer edge coordinates, computes extents, and accepts negative virtual-screen coordinates where right/bottom exceed left/top.
- `MonitorDisplayInfo` validates nonblank device ID and valid bounds and work-area extents, preserving the values immutably.
- Provider and positioner interfaces match the brief.
- The three named tests document Purpose, Coverage, and Target effect; invalid work area, invalid bounds, and blank device ID are covered without hardware dependencies.
