# Task 3 implementation report

**Branch:** `codex/wpf-tray-icon`  
**Worktree:** `C:\Users\ton20\.codex\worktrees\wpf-tray-icon\desktop-puppy`

## Scope and behavior

Implemented the internal `TrayContextMenuFactory`, dispatcher-owned `WinFormsTrayIconHost`, friend assembly declaration, embedded generic paw icon, and two STA-only factory tests. The host creates exactly one `NotifyIcon` and menu on `Application.Current.Dispatcher`, bridges the four menu commands, and does not create a WinForms top-level window, call `Application.Run`, or use Shell interop. WPF remains the process/message-loop owner. The tests create no `NotifyIcon`, never set `Visible`, show no UI, and inject no desktop input.

The approved menu is ordered **Show Desktop Puppy**, **Hide Desktop Puppy**, **Usage Statistics**, separator, **Exit Desktop Puppy**. The host cleanup detaches factory click delegates and host event subscriptions, makes the icon non-visible, clears its menu reference, disposes the `NotifyIcon`, then disposes the separately owned menu/items and loaded icon once. This order follows the .NET 10 `NotifyIcon` component ownership boundary and the project’s cited .NET 10 source: https://github.com/dotnet/winforms/blob/v10.0.0/src/System.Windows.Forms/System/Windows/Forms/NotifyIcon.cs . `Visible` is only the requested component state; no claim of Shell registration/visibility is made.

## TDD evidence

### RED (before `TrayContextMenuFactory` existed)

Ran restore as specified:

```powershell
$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.dotnet-home')
& 'C:\Program Files\dotnet\dotnet.exe' restore DesktopPuppy.sln --ignore-failed-sources -p:NuGetAudit=false
```

Restore completed. It emitted NU1801 because the configured local source `C:\Program Files (x86)\Microsoft SDKs\NuGetPackages\` does not exist; the framework/package restore was otherwise usable.

Then ran the exact focused command:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~TrayContextMenuFactoryTests' --verbosity minimal -p:NuGetAudit=false --maxcpucount:1 --nodeReuse:false
```

Expected compile-time RED, with no test execution. The only compiler errors in the test source were the two intended missing-factory references:

```text
tests/DesktopPuppy.Tests/Pet/Tray/TrayContextMenuFactoryTests.cs(18,30): error CS0103: 当前上下文中不存在名称“TrayContextMenuFactory”
tests/DesktopPuppy.Tests/Pet/Tray/TrayContextMenuFactoryTests.cs(34,30): error CS0103: 当前上下文中不存在名称“TrayContextMenuFactory”
```

### GREEN

The exact focused command above was rerun after implementation. Result:

```text
总共 1 个测试文件与指定模式相匹配。
已通过! - 失败:     0，通过:     2，已跳过:     0，总计:     2
```

The tests cover the actual menu item text/type order and exactly-once command callback order. They are not Shell-integration tests.

## Icon evidence

`DesktopPuppyTray.ico` is an original generic paw mark (not character art or the executable icon). ICO directory parsing found exactly two entries: **16×16** and **32×32**. Windows `System.Drawing.Icon` loaded each requested size, and `ToBitmap()` rendered exact results:

```text
System.Drawing.Icon request 16x16: loaded=16x16, rendered=16x16
System.Drawing.Icon request 32x32: loaded=32x32, rendered=32x32
ICO image entries: 16x16, 32x32
```

The app project embeds it under `DesktopPuppy.Pet.Tray.Assets.DesktopPuppyTray.ico`.

## Application build and final focused tests

Ran the brief’s exact Release app build:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build src/DesktopPuppy/DesktopPuppy.csproj --configuration Release --no-restore --no-incremental --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false -p:NuGetAudit=false
```

Result: **exit 0, 0 errors, 26 warnings**. The warning texts (with observed occurrence counts) were:

```text
24x: `C:\Users\ton20\.codex\worktrees\wpf-tray-icon\desktop-puppy\src\DesktopPuppy\DesktopPuppy.csproj : warning NU1801: 本地源“C:\Program Files (x86)\Microsoft SDKs\NuGetPackages\”不存在。`
24x: `C:\Users\ton20\.codex\worktrees\wpf-tray-icon\desktop-puppy\src\DesktopPuppy\DesktopPuppy_pjkcngt3_wpftmp.csproj : warning NU1801: 本地源“C:\Program Files (x86)\Microsoft SDKs\NuGetPackages\”不存在。`
2x: `CSC : warning WFO0003: 从 C:\Users\ton20\.codex\worktrees\wpf-tray-icon\desktop-puppy\src\DesktopPuppy\app.manifest 中删除高 DPI 设置，并通过 Application.SetHighDpiMode API 或“ApplicationHighDpiMode”项目属性进行配置 (https://aka.ms/winforms-warnings/wfo0003) [C:\Users\ton20\.codex\worktrees\wpf-tray-icon\desktop-puppy\src\DesktopPuppy\DesktopPuppy.csproj]`
2x: `CSC : warning WFO0003: 从 C:\Users\ton20\.codex\worktrees\wpf-tray-icon\desktop-puppy\src\DesktopPuppy\app.manifest 中删除高 DPI 设置，并通过 Application.SetHighDpiMode API 或“ApplicationHighDpiMode”项目属性进行配置 (https://aka.ms/winforms-warnings/wfo0003) [C:\Users\ton20\.codex\worktrees\wpf-tray-icon\desktop-puppy\src\DesktopPuppy\DesktopPuppy_pjkcngt3_wpftmp.csproj]`
```

The generated WPF temporary-project path above is the exact identifier observed in this final verification build. NU1801 is the preconfigured missing local package source. WFO0003 is the existing manifest/high-DPI setting warning surfaced by enabling the WinForms desktop framework. Neither was treated as a build failure.

Ran the brief’s exact focused tests after the build:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~TrayContextMenuFactoryTests' --verbosity minimal -p:NuGetAudit=false --maxcpucount:1 --nodeReuse:false
```

Result: **exit 0; 2 passed, 0 failed, 0 skipped**.

Actual Shell icon presence/menu positioning and Explorer behavior remain unverified and require manual acceptance. Neither a build nor `NotifyIcon.Visible` proves Shell visibility.

## Follow-up fix — resilient host cleanup

**Fix branch commit before this change:** `9fc0c14 docs: harden tray resource cleanup plan` (feature baseline: `0157228`).

### TDD RED

Added `Cleanup_continues_after_one_action_throws` to `tests/DesktopPuppy.Tests/Pet/Tray/TrayResourceCleanupTests.cs` before creating the helper or modifying the host, then ran the updated brief’s restore and focused filter. Restore completed (with the existing NU1801 missing-local-source warnings). The focused test build stopped at the expected compile-time missing-helper reference; the intended test failure was:

```text
tests/DesktopPuppy.Tests/Pet/Tray/TrayResourceCleanupTests.cs(16,46): error CS0103: 当前上下文中不存在名称“TrayResourceCleanup”
```

The two existing menu tests also referenced the absent factory during the initial attempt? **No** — the factory already existed from the feature commit. The RED was solely the new `TrayResourceCleanup` type missing.

### GREEN and final verification

After adding the helper and updating `WinFormsTrayIconHost`, ran the updated brief’s focused filter:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~TrayContextMenuFactoryTests|FullyQualifiedName~TrayResourceCleanupTests' --verbosity minimal -p:NuGetAudit=false --maxcpucount:1 --nodeReuse:false
```

Result: **exit 0; 3 passed, 0 failed, 0 skipped**. This includes menu labels/order, command mapping, and proof that a throwing cleanup action does not prevent the following action. These tests create no `NotifyIcon` and perform no real Shell or desktop interaction.

Ran the updated brief’s Release app build:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build src/DesktopPuppy/DesktopPuppy.csproj --configuration Release --no-restore --no-incremental --maxcpucount:1 --nodeReuse:false -p:UseSharedCompilation=false -p:NuGetAudit=false
```

Result: **exit 0; 0 errors; 26 warnings**. These are the exact distinct diagnostic lines observed (the generated WPF temporary-project identifier is `DesktopPuppy_if4dy4f4_wpftmp.csproj`):

```text
C:\Users\ton20\.codex\worktrees\wpf-tray-icon\desktop-puppy\src\DesktopPuppy\DesktopPuppy.csproj : warning NU1801: 本地源“C:\Program Files (x86)\Microsoft SDKs\NuGetPackages\”不存在。
C:\Users\ton20\.codex\worktrees\wpf-tray-icon\desktop-puppy\src\DesktopPuppy\DesktopPuppy_if4dy4f4_wpftmp.csproj : warning NU1801: 本地源“C:\Program Files (x86)\Microsoft SDKs\NuGetPackages\”不存在。
CSC : warning WFO0003: 从 C:\Users\ton20\.codex\worktrees\wpf-tray-icon\desktop-puppy\src\DesktopPuppy\app.manifest 中删除高 DPI 设置，并通过 Application.SetHighDpiMode API 或“ApplicationHighDpiMode”项目属性进行配置 (https://aka.ms/winforms-warnings/wfo0003) [C:\Users\ton20\.codex\worktrees\wpf-tray-icon\desktop-puppy\src\DesktopPuppy\DesktopPuppy.csproj]
CSC : warning WFO0003: 从 C:\Users\ton20\.codex\worktrees\wpf-tray-icon\desktop-puppy\src\DesktopPuppy\app.manifest 中删除高 DPI 设置，并通过 Application.SetHighDpiMode API 或“ApplicationHighDpiMode”项目属性进行配置 (https://aka.ms/winforms-warnings/wfo0003) [C:\Users\ton20\.codex\worktrees\wpf-tray-icon\desktop-puppy\src\DesktopPuppy\DesktopPuppy_if4dy4f4_wpftmp.csproj]
```

The build was followed by the same focused test command; result: **exit 0; 3 passed, 0 failed, 0 skipped**.

### Changes and limitations

- `Start()` assigns `_notifyIcon` immediately after construction and before setting component properties, so a later property failure is covered by owned-resource cleanup.
- `Dispose()` clears its fields before invoking six ordered, independent best-effort actions. The helper continues after an individual failure and writes only the fixed diagnostic `Tray resource cleanup step failed.`; it does not expose exception details.
- Both `Start()` and `Dispose()` explicitly throw `InvalidOperationException("A current WPF Application is required to use the tray icon host.")` when no current WPF application exists, before dispatcher access or host mutation.
- No `App` integration changes were made. Actual Explorer/Shell icon visibility and menu interaction remain unverified and require manual acceptance; no test or `NotifyIcon.Visible` value is claimed as evidence of Shell visibility.
