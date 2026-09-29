# Task 4 report — Per-Monitor DPI manifest and WPF placement wiring

## Scope and base

Implemented only Task 4 in `C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy`, branch `codex/windows-e2e-acceptance`, based on `74ed0e4dc452c560ef389d1ca38eb068c12c0238`. The working tree was clean before Task 4. Changes are restricted to the Task 4 project/app/window files and this report.

No tests were added: the approved Task 4 file map excludes test files, and changing tests or other tasks' files would violate the explicit Task 4-only boundary. Accordingly, there is no Task 4 test RED run. The existing full deterministic xUnit project was run after the final code change.

## Implementation

- Created `src/DesktopPuppy/app.manifest` and attached it using `<ApplicationManifest>app.manifest</ApplicationManifest>` in `src/DesktopPuppy/DesktopPuppy.csproj`. It declares `PerMonitorV2,PerMonitor` in the Windows 10 DPI-awareness namespace and the legacy `true/pm` fallback. The manifest is selected as both `Win32Manifest` and `EmbeddedWin32Manifest` by the SDK build target, before WPF creates the HWND. No runtime DPI-awareness P/Invoke was added.
- `App` constructs and owns `Win32MonitorDisplayProvider` and `PetWindowPlacementService`, injecting them into `PetWindow`. Initialization exceptions clean up a partially created provider, are diagnosed, and leave both dependencies null so the offline window starts normally.
- The app composes a one-shot primary-monitor startup selector around the existing `Win32WindowPositioner`: the first monitor selection made by `ApplyInitial` returns the enumerated primary monitor, so the existing WPF `CenterScreen` bounds are treated as the request and clamped against the primary physical work area. Subsequent selections delegate to the Native positioner, preserving normal cross-monitor reconciliation.
- `PetWindow` captures its HWND at `SourceInitialized`, applies `ApplyInitial` on `Loaded`, and queues reconciliation after location/DPI changes. Provider display-change notifications are marshalled onto the WPF Dispatcher and coalesced. Exceptions from initial placement or reconciliation are caught and diagnosed; the code does not reset the WPF position when native placement fails.
- On actual `Closed` (not a cancellable `Closing`), `PetWindow` unsubscribes from display notifications and calls `Forget(hwnd)`. `App.OnExit` disposes the owned service and provider.
- Existing `CenterScreen`, `Topmost`, drag/interaction code and `GetDoubleClickTime` are unchanged. The existing HWND adapter still uses `SWP_NOACTIVATE | SWP_NOZORDER`; no activation or z-order changes were introduced.

## Verification commands and exact results

### Release app build

Command:

```text
dotnet build src/DesktopPuppy/DesktopPuppy.csproj --configuration Release -m:1 /nodeReuse:false
```

Final run output:

```text
正在确定要还原的项目…
所有项目均是最新的，无法还原。
  DesktopPuppy -> C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\src\DesktopPuppy\bin\Release\net10.0-windows\DesktopPuppy.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:12.14
```

Exit code: `0`.

### Full deterministic xUnit suite

Command:

```text
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj --configuration Release -m:1 /nodeReuse:false
```

Final output summary:

```text
C:\Users\ton20\.codex\worktrees\windows-e2e-acceptance\desktop-puppy\tests\DesktopPuppy.Tests\bin\Release\net10.0-windows\DesktopPuppy.Tests.dll (.NETCoreApp,Version=v10.0)的测试运行
总共 1 个测试文件与指定模式相匹配。
已通过! - 失败:     0，通过:   150，已跳过:     0，总计:   150，持续时间: 1 s - DesktopPuppy.Tests.dll (net10.0)
```

Exit code: `0`.

### Manifest and native-boundary checks

Command:

```text
dotnet msbuild src/DesktopPuppy/DesktopPuppy.csproj -target:SetWin32ManifestProperties -getProperty:Win32Manifest,EmbeddedWin32Manifest
```

Output:

```json
{
  "Properties": {
    "Win32Manifest": "app.manifest",
    "EmbeddedWin32Manifest": "app.manifest"
  }
}
```

Command:

```text
[xml](Get-Content -LiteralPath 'src\DesktopPuppy\app.manifest') | Out-Null; Write-Output 'Manifest XML: valid'
```

Output: `Manifest XML: valid`.

Command:

```text
rg -n '\[DllImport|\[LibraryImport|SetProcessDpiAwareness|SetThreadDpiAwarenessContext' src/DesktopPuppy
```

Inspection result: all display/HWND and other native declarations remain under `src/DesktopPuppy/Native`, except the pre-existing, unrelated `GetDoubleClickTime` declaration at `src/DesktopPuppy/Pet/PetWindow.xaml.cs:17`; that declaration is unchanged. No runtime process/thread DPI-awareness API matched.

Command:

```text
git diff --check
```

Output: no output; exit code `0`.

## UI and hardware verification boundaries

- **UI smoke / `Category=Ui`: Not run.** The ledger safety ruling forbids running the existing interaction tests against the live desktop because they use `SendInput`. No drag, interaction, or statistics-window behavior is claimed as runtime-verified by this Task 4 run.
- No UI/interaction smoke test was run, no display topology or scaling was changed, and no real multi-monitor acceptance workflow or live HWND placement was performed.
- The Release build and deterministic tests verify compilation and existing deterministic contracts only; manifest loading, WPF lifecycle behavior, and physical monitor/DPI behavior were not runtime exercised.

## Changed files

- `src/DesktopPuppy/app.manifest` (created)
- `src/DesktopPuppy/DesktopPuppy.csproj`
- `src/DesktopPuppy/App.xaml.cs`
- `src/DesktopPuppy/Pet/PetWindow.xaml.cs`
- `.superpowers/sdd/2026-09-28-monitor-dpi-placement-acceptance-plan/task-4-report.md` (this report)

## Concerns

No build or deterministic test failure remains. The actual WPF/native path still needs the separately scoped safe UI/hardware acceptance when it can be run without injecting input into the user's live desktop. No such acceptance is implied by this report.
