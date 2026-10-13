# 剩余验收与下一步

**最新进展：** 用户随后授权开始测试。最新发布版完整单元套件 365 项通过，完整 UI/E2E 同一次运行 32 项通过，均无失败或跳过。统计修正后的 UI 缺口已补齐，见 `2026-10-13-post-fix-regression.md`。以下“不影响桌面”与“UI 尚未重跑”描述为本次授权前的历史状态；真实系统操作及人工体验仍待验收。

用户当前选择：只做不影响电脑使用的检查。因此不启动交互工具或桌宠、不注入输入、不修改真实启动项、不锁屏、不休眠、不重启 Explorer、不改变显示器连接或缩放。

功能代码已经实现；进一步后台复核补正了 AI 展示事实：日报实际查看也计入展示，跨午夜按实际展示日期归属，截止后的展示不进入截止快照。三个回归用例先失败，修正后完整单元/SQLite/HTTP 合约套件 365 项通过。

最新版已发布至 `artifacts/mvp`，没有启动。旧版完整 UI/E2E 有 32 项成功记录；本次统计修正后的 UI 回归尚未重跑，以遵守用户暂不影响桌面的要求。之前通过 UI 的版本保留在 `.local/mvp-before-display-facts-fix`。

最新发布 DLL 与完成 365 项测试的 DLL 哈希一致：`75319C0373F231ECDF61899BB8A058EE88F1971AA8A48BAAEBC16A2E86093224`。

## 后台已经完成

- 新统计回归及完整 365 项测试；全部使用隔离数据。
- Release 发布、交付文件检查。
- 现有 Windows 验收控制器已发布至 `artifacts/windows-acceptance`，仅构建，未启动。
- 制作会话/显示器验收启动脚本；仅未来由用户执行时启用测试，不改变父进程环境。

## 尚未完成

| 项目 | 需要的操作 | 当前状态 |
|---|---|---|
| 本次统计修正后的 UI/E2E 回归 | 已授权并运行隔离窗口和鼠标输入 | Passed；最新发布版同一次运行 32 项全部通过 |
| 五种 AI 场景的实际界面体验 | 服务管线已经联调，仍需确认实际动画、文字、日报与通知体验 | Not run（界面体验）；Provider 管线 Passed |
| 真实登录启动 | 正常退出后在设置启用启动项，并由用户注销/重新登录 | Not run |
| 实际游戏/视频全屏 | 用户进入/退出全屏，确认隐藏、恢复与统计继续；隐藏意图不被恢复覆盖 | Not run |
| 锁屏/解锁 | 验收控制器监听事件，由用户按 Win+L 并自行解锁 | Not run |
| 睡眠/休眠及恢复 | 用户保存工作后自行执行电源操作 | Not run |
| 混合 DPI 与拔屏恢复 | 至少两块实际显示器；用户拖动、调整连接并确认 | Not run |
| Explorer 重启 | 用户自行重启 Shell，检查托盘恢复与无重复实例 | Not run |
| CSV 在 Excel 打开 | 当前系统未注册 Excel；需要实际 Excel 环境 | Not run |

## 将来准备好时使用控制器

先正常退出已有桌宠。工具若发现任何 DesktopPuppy 进程会显示 Skipped，不会关闭或操作它。

- 会话验收：双击 `artifacts/windows-acceptance/开始会话验收.cmd`。
- 确实准备操作显示器时：双击 `artifacts/windows-acceptance/开始显示器验收.cmd`。
- 控制器会先显示场景说明；用户选择场景并点击 Begin 才创建隔离桌宠和临时数据库。
- 工具不会主动锁屏、休眠、输入密码、断开显示器或更改系统设置。
- 英文按钮：Run lock/unlock acceptance（锁屏），Run suspend/resume acceptance（电源），Run mixed-DPI display acceptance（混合 DPI），Run monitor-removal recovery acceptance（拔屏）。
- 最终只记录真实观察到的 Passed / Failed；缺少条件为 Skipped，取消或超时为 Incomplete，不作为通过。

当前可复核的基线与所有原有记录见 `2026-10-13-mvp-acceptance.md`。本文件是其后续补充，不将旧版本 UI 成功冒充本次修正后的运行结果。
