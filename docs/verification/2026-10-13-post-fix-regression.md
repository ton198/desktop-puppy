# 最新发布版回归 — 2026-10-13

用户授权开始测试后，执行完整单元套件和最新版交付程序 UI/E2E 回归。测试使用隔离数据库。本轮没有修改产品代码、提交、合并或推送。

| 检查 | 本次结果 | 证据 |
|---|---|---|
| Release 单元 / SQLite / HTTP 合约 | 365 Passed，0 Failed，0 Skipped；约 8 秒 | `tests/DesktopPuppy.Tests/TestResults/next-step-unit-diagnostic.trx` |
| 发布版 UI 与实时统计刷新 E2E | 同一次运行 32 Passed，0 Failed，0 Skipped；约 2 分 32 秒 | `tests/DesktopPuppy.UiTests/TestResults/next-step-delivery-ui.trx` |

首次受限环境单元运行因 vstest 未能在 90 秒内连接 testhost 而中止，见 `next-step-unit.trx`；该次不是通过记录。在允许 Windows 测试进程通信的环境中重跑完整套件后通过，诊断日志为 `artifacts/next-step-unit-diag.log`。

UI 命令使用 `Category=Ui|Category=LongRunningE2E`，`DESKTOP_PUPPY_EXE` 指向 `artifacts/mvp/DesktopPuppy.exe`。两次测试均使用 Release、无恢复；UI 测试构建采用单进程 MSBuild、关闭节点复用及共享编译。

交付 DLL 和本次测试构建 DLL 的 SHA-256 相同：`75319C0373F231ECDF61899BB8A058EE88F1971AA8A48BAAEBC16A2E86093224`。本次补齐统计修正后的完整 UI 回归。

15 分钟 Performance 测量本轮未重复，仍沿用之前的独立记录。没有执行真实 Provider 网络联调、登录、全屏应用、锁屏、睡眠、多屏 DPI、拔屏、Explorer 重启或 Excel 人工验收；五种 AI 场景的人工界面体验及动画观感仍待用户确认。
