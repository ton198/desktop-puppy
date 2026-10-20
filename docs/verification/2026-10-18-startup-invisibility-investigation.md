# 0.1.0 在其他电脑上不显示桌宠：调查记录

调查日期：2026-10-18（America/New_York）。本次任务是调查；没有修改产品代码或重新制作安装包。

## 结论与确定程度

**已确认发布程序存在全屏误判造成启动始终隐藏的缺陷，并用交付 EXE 复现。它是用户报告的首要嫌疑，但目前还没有用户机的命中窗口或 ShellState 数据，不能宣称用户机的具体触发源已查实。**

问题发生在应用显示策略，不是复现环境中安装器不能安装或运行库不能启动。程序默认启用全屏隐藏，扫描所有顶层窗口；任何可见、未最小化、未 cloaked、没有被指定样式判为有框、非已列举桌面类、尺寸覆盖显示器的窗口都会触发隐藏。透明、鼠标穿透、不可激活的后台工具窗口也能满足条件。显式“显示”、第二实例激活和等待结束均不覆盖全屏抑制。

报告已压制过 `Cua.AgentCursorOverlay`，所以**不能据本地覆盖窗口复现就认定该窗口是用户机元凶**。仍需同时排查其他命中窗口和 Shell 返回值；原应用没有记录内部状态，远程报告也没有记录这些值。

## 版本和打包核对

- 当前 HEAD：`cdcb6d51ce53ad22209c70cc0843f1f4a9f3198e`，与来件报告一致。
- 直接检查 `artifacts/installer/publish/DesktopPuppy.dll`：FileVersion `0.1.0.0`；ProductVersion `0.1.0+cdcb6d51ce53ad22209c70cc0843f1f4a9f3198e`。
- 该 DLL SHA-256：`AEC6AA789EA0DB315E6A48146DEE0595DCD20570C07DD007DD17B22DE721EF26`。未取得对方文件的哈希，不能证明双方安装文件字节完全一致。
- `scripts/build-installer.ps1` 使用 `win-x64 --self-contained true`；Inno Setup 递归打包发布目录。
- 发布目录 runtimeconfig 声明包含 .NETCore.App 和 WindowsDesktop.App `10.0.12`。对方已有托盘和 SQLite 写入，与“进程已经成功运行”一致。
- 正常帧通过 DLL 内嵌资源加载，不依赖开发机的素材绝对路径。本地从交付 DLL 成功读出 28 个正常动画帧；实测 EXE 可以显示。尚未对用户机文件做完整性比对。

## 已验证的显示路径

1. `src/DesktopPuppy/App.xaml.cs:80`：先 `SetStartupWaiting(true)`，阻止过早显示。
2. `App.xaml.cs:375`：初始化全屏检测；每 500 ms 刷新全屏抑制状态。
3. `src/DesktopPuppy/Native/FullscreenDetector.cs:19`：若 ShellState 为 3，直接抑制；否则枚举**全部**顶层窗口，遇到第一个符合几何/样式规则的窗口即返回真。
4. `src/DesktopPuppy/App.Features.cs:83`：普通启动立即结束等待；只有带 `--startup` 的启动才走 10 秒延迟。托盘显示也会解除等待。
5. `src/DesktopPuppy/Pet/PetWindow.xaml.cs:85`：`ShowFromUser` 只清除用户隐藏标志；`ApplyVisibility` 仍要求没有全屏抑制，否则不调用 WPF `Show()`。
6. `src/DesktopPuppy/Ai/SingleBubbleRequestGate.cs:21`：实际窗口使用的 `PetVisibilityState.ShouldShow` 同时检查用户隐藏、全屏和启动等待。

所以“等待超过 10 秒”“再次启动”“托盘显示无效”均与全屏标志持续为真相容。启动延迟结束并不保证全屏抑制期间显示。

还有两个加重排查难度的问题：

- `CheckFullscreen()` 捕获异常后保留上次观察值，没有日志；若此前为真，后续持续检测异常会延续隐藏。尚未证实用户机发生此路径。
- `InitializeFullscreen()` 首次设置没有应用 `SuppressOnFullscreen` 配置开关，后续定时器才应用。关闭配置时通常应在下一个 500 ms 周期恢复，不能单独解释默认配置下永久隐藏。

ShellState 的 3 对应独占 Direct3D 全屏，2 为 busy，应用仅对 3 直接隐藏。定义依据：[Microsoft QUERY_USER_NOTIFICATION_STATE 文档](https://learn.microsoft.com/zh-cn/windows/win32/api/shellapi/ne-shellapi-query_user_notification_state)。本地复现期间 ShellState 为 2，因此本地实验确实走了窗口枚举分支。

## 实验和证据

探针源代码及输出位于 `artifacts/startup-investigation/`，全部使用隔离对象或新建的隔离数据库，没有读写正常用户数据库。初次探针宿主遇到资源 URI 的宿主程序集差异；调整探针资源后运行成功，此宿主问题不作为产品缺陷证据。

### 交付 DLL 状态实验

使用 `artifacts/installer/publish/DesktopPuppy.dll` 中的实际类型：

```text
frames=28
ordinaryBorderlessOverlaySuppresses=True
desktopExcluded=True
cloakedExcluded=True
blocked: visible=False, loaded=0, handle=0
delayCompleted: waiting=False, visible=False, loaded=0
unblocked: visible=True, loaded=1, imagePresent=True
```

启动等待解除、显示请求发出后，全屏状态仍让窗口不可见，首次 Loaded 和主窗口 HWND 创建都未发生；解除全屏状态后图像存在。这是已验证机制，不是用户机内部状态的直接采样。

### 交付 EXE 集成复现

运行未修改的 `artifacts/installer/publish/DesktopPuppy.exe`，指定位于中文目录 `中文用户-<随机标识>/desktop-puppy.db` 的新数据库。另一个进程创建透明、不可激活、鼠标穿透、不进入任务栏的无边框全屏工具窗口：

```text
actualExeWithTransparentToolOverlay: running=True, visibleWindows=0, dbExists=True
actualExeAfterOverlayClosed: running=True, visibleWindows=1
```

第一阶段保持 12 秒；移除覆盖窗口后等待 2 秒。仅改变覆盖窗口存在与否，就能使交付 EXE 从始终隐藏恢复显示。实验进程已结束。

这证明发布程序会被这类后台窗口误抑制，同时证明中文数据库路径可在本机工作；**不等价于复现中文 Windows 用户安装路径、150% DPI、混合显卡的完整用户环境**。

### 其他验证

- 现有全屏和启动状态相关测试：13 Passed，0 Failed，0 Skipped。命令使用 Release、`--no-build --no-restore`，执行既有构建，不代表本次重新构建或全量回归。
- TRX：`tests/DesktopPuppy.Tests/TestResults/startup-investigation.trx`。
- 首次受限环境运行未完成测试通信，已取消；随后在允许进程通信的环境执行成功，不把首次运行计为通过。
- 新诊断脚本已在 PowerShell 7 与系统 Windows PowerShell 执行成功；正常环境报告 `WouldSuppress=False`，覆盖窗口存在时报告 `WouldSuppress=True` 并列出其 HWND/类名。
- 本地采集输出：`live-diagnostics.json`、`overlay-diagnostics.json`、`windows-powershell-diagnostics.json`。

## 对来件报告的判断修正

| 报告观察/怀疑 | 代码和实验能支持的结论 |
| --- | --- |
| 全程无 SHOW，后台统计和托盘存在 | 显示策略阻止首次 Show 是强嫌疑；不是“整个程序没有启动”。 |
| 强制显示某个 WPF HWND 后全透明 | 不能单独证明资源加载失败。被阻止首次 Show 的实际 PetWindow 可连 HWND 都没有；枚举到的隐藏 WPF HWND 也可能是辅助窗口。Win32 强显不等同于 WPF 正常显示生命周期。 |
| `pet_placement` 为 0 行 | 位置在拖动结束、重置位置和退出等路径保存，非窗口构造成功即写入。空表不能证明资源初始化失败。 |
| 14 条日报均 `StatisticsUnavailable` | 日报会回补最多过去 14 天；新用户无历史数据时可以如此，不足以证明数据库或渲染失败。见 `DailyReportService.cs:15`。 |
| 任务栏自动隐藏，工作区等于屏幕 | 全屏判定使用显示器 Bounds，不比较 WorkArea 和 Bounds 是否相等；该等式本身不会触发隐藏。 |
| cloaked UWP、Progman、WorkerW | 当前代码已经过滤 cloaked 和这些桌面类，不能直接将它们认定为误判来源。DWM 查询失败或其他未过滤系统窗口需实采。 |
| 中文用户名或混合显卡 | 暂无直接致因证据，不能要求改用户名、显卡或重装运行库来代替验证。 |
| 诊断开关无输出 | 已确认：`DESKTOP_PUPPY_TEST_DIAGNOSTICS=1` 仅向动画 UIA HelpText 写计数，没有启动文件日志或全屏命中日志。WinExe 的 Console.Error 和 Release 中通常移除的 Debug.WriteLine 也不是可靠用户诊断渠道。 |

## 用户机最后一步取证

新增 `scripts/diagnose-startup.ps1`，默认采集 5 次。保持故障中的桌宠运行，在脚本所在目录执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\diagnose-startup.ps1
```

会在当前目录生成 `DesktopPuppy-startup-diagnostics.json`。不需要管理员权限，不启动/结束应用，不读取数据库内容或窗口标题，不修改注册表或应用配置，仅临时设置并恢复采集线程 DPI 上下文，保证物理坐标准确。

解释结果：

- `WouldSuppress=True` 且有 `Candidate=True`：记录具体 HWND、进程、类名、样式、尺寸；这是旧策略会命中的窗口，仍需与用户发生故障的时段一致。
- `ShellResult=0` 且 `ShellState=3`：即使无窗口命中，旧程序也会隐藏。
- 多次 `WouldSuppress=False` 仍不显示：不能硬套本地覆盖窗口原因；需增加应用内部文件日志，确认 StartupWaiting、UserHidden、Fullscreen、初始化进度与检测异常，必要时采集托管线程栈。
- 外部脚本不读取应用内部缓存的 `_lastFullscreen`，不能排除“旧值为真、检测持续异常”的路径。受进程权限限制时进程名可能为 `unavailable`，仍保留 HWND、PID 和窗口类。

也可手动通过托盘设置关闭“全屏隐藏”，观察 500 ms 后是否显示，作为独立对照；无需先改显卡、任务栏或数据库。该对照本次未在用户机执行。

## 修复方向与验收缺口

后续修复应先明确需要抑制的前台全屏应用范围，避免把任意后台工具/透明覆盖层等同于全屏游戏，并保留多屏全屏场景的产品要求；为显式“显示”提供恢复或诊断入口；加入不包含标题、Key、正文的启动和隐藏原因文件日志。不能靠硬编码某一个机器上的进程名解决所有环境。

现有测试验证了“输入一个全屏状态后应该隐藏”，但没有验证“真实桌面后台覆盖层不应被当作全屏应用”。此前验收记录也将真实全屏/硬件环境列为 Not run；安装成功、本机测试通过，不能证明另一台电脑的显示策略正确。修复后必须使用实际安装包验收：首次启动、后台透明覆盖层、任务栏自动隐藏、用户报告的 DPI、显式显示、正常全屏进入/退出和多屏恢复。
