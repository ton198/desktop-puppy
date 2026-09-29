# Task 2 report — Native display and HWND adapters

## Scope and base

Implemented only Monitor/DPI placement plan Task 2, starting from `b276ca79317c0778800ed07421e3b416b288b816` (`b276ca7`, Task 1 geometry contracts). The unrelated main workspace was not changed.

## TDD evidence

**RED** — added the four requested fake-driven tests before the adapter implementations, then ran:

`dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --filter "FullyQualifiedName~Win32MonitorDisplayProviderTests|FullyQualifiedName~Win32WindowPositionerTests" -m:1 /nodeReuse:false`

The expected RED build failure was missing adapter types: `IWin32DisplayApi`, `IWin32WindowApi`, `NativeMonitorInfo`, and `IDisplaySettingsChangedSource` (CS0246). No hardware access occurred.

**GREEN** — final focused runs:

`dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --filter "FullyQualifiedName~Win32MonitorDisplayProviderTests" -m:1 /nodeReuse:false`

Exact result:

`已通过! - 失败:     0，通过:     3，已跳过:     0，总计:     3，持续时间: 57 ms - DesktopPuppy.Tests.dll (net10.0)`

`dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --filter "FullyQualifiedName~Win32WindowPositionerTests" -m:1 /nodeReuse:false`

Exact result:

`已通过! - 失败:     0，通过:     1，已跳过:     0，总计:     1，持续时间: 40 ms - DesktopPuppy.Tests.dll (net10.0)`

Release build:

`dotnet build src/DesktopPuppy/DesktopPuppy.csproj --configuration Release -m:1 /nodeReuse:false`

Exact result:

```text
已成功生成。
    0 个警告
    0 个错误
```

## Implementation notes

- `Win32DisplayApi` wraps `EnumDisplayMonitors` and `GetMonitorInfoW` and returns the physical-pixel monitor/work rectangles, native device name, and primary flag. Callback exceptions are caught at the unmanaged boundary and rethrown after enumeration.
- `Win32MonitorDisplayProvider` converts native records into validated `MonitorDisplayInfo` entries, rejects enumeration/info failures, empty results, invalid geometry, and anything other than exactly one primary monitor. It keeps a handle-to-instance dictionary. `Win32WindowPositioner` asks its fakeable `IWin32WindowApi` for `MonitorFromWindow`, then resolves that handle through the provider dictionary (refreshing enumeration once if an unseen handle is returned). Its result is the same in-memory object/device ID returned by `GetMonitors()`.
- `Win32WindowApi` wraps `GetWindowRect`, `MonitorFromWindow`, `GetDpiForWindow`, and `SetWindowPos`. It passes `SWP_NOACTIVATE | SWP_NOZORDER` (`0x0010 | 0x0004`) to `SetWindowPos`. The positioner surfaces zero DPI, unknown monitor handles, invalid bounds, and failed native calls as exceptions; no system-DPI or geometry defaults are invented.
- The provider subscribes to the display-settings source once, forwards the event without dispatcher/WPF work, and uses idempotent disposal to unsubscribe and ignore any late callbacks.
- All tests use fake native handles/APIs and a fake event source. They neither inspect nor modify attached monitors, display settings, topology, or real HWNDs. Negative-origin physical rectangles remain accepted by Task 1 contracts.

## Four requested test cases

1. `Provider_maps_primary_secondary_and_negative_origin_monitors`
   - **Purpose:** Ensure Windows' enumeration order does not dictate primary selection or discard left-side monitors.
   - **Coverage:** Fake monitor handles with one primary and one negative-origin secondary.
   - **Target effect:** Both work areas and the primary flag map correctly.
2. `Provider_reports_native_enumeration_failure_without_fabricating_a_monitor`
   - **Purpose:** Avoid silently positioning from invented geometry.
   - **Coverage:** Native enumeration failure and no-primary response.
   - **Target effect:** A clear failure is returned to the caller; no default rectangle is treated as a monitor.
3. `Provider_unsubscribes_display_change_handler_on_dispose`
   - **Purpose:** Avoid retaining windows or receiving callbacks during shutdown.
   - **Coverage:** Subscribe, dispose, then raise a fake display change.
   - **Target effect:** No callback reaches a disposed placement client.
4. `Window_positioner_reads_physical_bounds_monitor_and_actual_dpi`
   - **Purpose:** Keep HWND measurements in one physical coordinate/DPI context.
   - **Coverage:** Fake `GetWindowRect`, monitor lookup and `GetDpiForWindow` responses, including zero/failed native-call results.
   - **Target effect:** Returned values match the native adapter output and zero/failed DPI is surfaced as failure.

## Touched files

- `src/DesktopPuppy/Native/IWin32DisplayApi.cs`
- `src/DesktopPuppy/Native/IWin32WindowApi.cs`
- `src/DesktopPuppy/Native/Win32DisplayApi.cs`
- `src/DesktopPuppy/Native/Win32MonitorDisplayProvider.cs`
- `src/DesktopPuppy/Native/Win32WindowApi.cs`
- `src/DesktopPuppy/Native/Win32WindowPositioner.cs`
- `tests/DesktopPuppy.Tests/Native/Win32MonitorDisplayProviderTests.cs`
- `tests/DesktopPuppy.Tests/Native/Win32WindowPositionerTests.cs`
- `.superpowers/sdd/2026-09-28-monitor-dpi-placement-acceptance-plan/task-2-report.md`
