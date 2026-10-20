# 0.1.1 全屏误隐藏修复与交付

日期：2026-10-18（America/New_York）。用户授权修复已确认的启动显示缺陷并重新打包。

## 行为变化

- 全屏窗口识别增加扩展样式检查，过滤 `WS_EX_TRANSPARENT`、`WS_EX_TOOLWINDOW`、`WS_EX_NOACTIVATE` 覆盖窗口，避免后台覆盖层使桌宠从启动起一直隐藏。
- 保留任意显示器上的普通无边框全屏和独占 Direct3D 全屏抑制；`WS_EX_TOPMOST` 或 `WS_EX_LAYERED` 单独存在不会被当作覆盖窗口排除。
- 首次全屏状态设置和首次请求资格判断均遵守“全屏隐藏”开关，与后续轮询一致。
- 增加持久化 `startup.log`，记录启动阶段、显示决策、全屏原因与检测异常类型。日志不包含窗口标题、Key、正文或数据库内容；单文件约 256 KiB 轮换，保留一份旧日志。
- 版本号、打包默认版本及使用说明更新为 0.1.1。安装器 AppId、用户数据位置和数据库结构不变。

代码审查未发现需要修正的产品代码问题；根据审查意见补充了“关闭全屏隐藏时，第一次显示判断就应通过”的实际 EXE 回归。

## 先失败、后修复的证据

| 验证 | 修复前结果 | 证据 |
| --- | --- | --- |
| 四种全屏覆盖窗口扩展样式 | 4 个断言失败；旧逻辑仍抑制桌宠 | `tests/DesktopPuppy.Tests/TestResults/fullscreen-overlay-red.trx` |
| 旧交付 EXE 在透明工具覆盖层存在时普通启动/延迟启动 | 2 项均等待可见窗口超时 | `tests/DesktopPuppy.UiTests/TestResults/fullscreen-overlay-exe-red.trx` |
| 暂时恢复首次检测旧代码，关闭全屏隐藏开关 | 首个结束等待后的显示决策仍为 False，断言失败 | `tests/DesktopPuppy.UiTests/TestResults/fullscreen-setting-red.trx` |

以上临时恢复已撤销，交付文件包含修复后的两个设置判断。

## 验证记录

- 最终产品源码完整单元 / SQLite / HTTP 合约套件：**372 Passed，0 Failed，0 Skipped**；`tests/DesktopPuppy.Tests/TestResults/fullscreen-011-unit.trx`。
- 安装包发布文件的三个新增 UI 用例全部通过：覆盖层存在时普通启动、覆盖层存在时 `--startup` 启动，以及关闭全屏隐藏时的首次显示；同时检查隔离目录中的实际启动日志。
- 常规 UI 初次完整运行：32 Passed、2 Failed。统计空状态用例失败于托盘图标定位，单独重跑通过；日报历史用例稳定失败于正文为空。
- 日报失败的原因是原测试默认选中昨天，但 18:20 后当天日报条目会成为默认选择。仅修改测试：显式加入当天无数据条目，并选择昨天已保存的样例，再验证正文。没有为此更改产品日报行为。
- 第二次完整常规 UI 运行：33 Passed、1 Failed；日报和统计均通过，剩余失败仍是测试辅助代码未展开托盘溢出面板。辅助代码原先在一次点击后就假定面板已打开；现优先使用 UIA Invoke，并在实际面板仍不存在时以一秒间隔重试。
- 最终定向 UI 复核：**6 Passed，0 Failed**，覆盖三个启动修复场景、托盘提前显示、统计空状态、日报历史。证据：`tests/DesktopPuppy.UiTests/TestResults/fullscreen-011-focused-final.trx`。本轮没有声称全部 34 项在最后一次完整运行中通过；两次完整运行及失败原因均保留。
- 中文路径烟测通过：把同一发布文件复制到 `.local/0.1.1-中文安装目录`，从项目根工作目录启动，数据库另置于中文测试目录。覆盖层存在时实际 EXE 保持运行、出现一个可见窗口，并记录 `Startup.FeaturesReady`；关闭覆盖层后仍可见。文件哈希与安装包发布目录一致，证据：`artifacts/startup-investigation/0.1.1-chinese-path-smoke.txt`。该测试不等同于在中文 Windows 账户或对方硬件上实测。
- 未重复 15 分钟 Performance 或长时间统计 E2E；未操作用户真实启动项、锁屏、电源、显示器配置或正常数据库。
- 编译保留已有 WFO0003 DPI manifest 警告；新增异步 UI 测试没有引入阻塞任务分析器警告。

## 安装包

- 文件：`artifacts/installer/DesktopPuppy-0.1.1-win-x64-Setup.exe`
- 大小：74,823,025 字节。
- SHA-256：`DC5DD653E04BFB602CEBCEFC401C9312A372C87702A021AB8857FDE884F67E28`
- 校验文件：同目录 `.exe.sha256`，已和实际文件核对。
- Inno Setup 编译成功；构建日志：`artifacts/startup-investigation/build-0.1.1.log`。
- 测试直接运行安装器使用的 `artifacts/installer/publish/DesktopPuppy.exe`；该目录在打包前由构建脚本核验绝对路径并清理，再完整自包含发布。
- 发布 DLL FileVersion：`0.1.1.0`；ProductVersion：`0.1.1+cdcb6d51ce53ad22209c70cc0843f1f4a9f3198e`。当前修复尚未提交，提交后缀不代表修复内容；以版本和本记录哈希识别交付文件。
- 发布 DLL SHA-256：`BEF9A038A6E16536B7380CB64A1393C56C1378007E30D764AD41510C6E0F3D66`。

本次未运行 Setup 安装到用户真实目录或更改其卸载注册信息；实际包体由相同 Inno 配置生成，应用验证针对包内发布文件。

## 对方电脑复测

1. 从托盘退出旧版，运行 0.1.1 安装包升级，普通启动观察是否出现桌宠。
2. 若仍不显示，在设置中暂时关闭“全屏隐藏”作对照，并提供 `%LOCALAPPDATA%\DesktopPuppy\startup.log`。
3. 普通启动应立即进入显示判断；只有 `--startup` 登录启动有 10 秒等待，仍受正常全屏隐藏策略约束。

本地已经修复并验证报告同类症状的一条真实故障路径；对方电脑具体触发窗口或 ShellState 尚未采集，不能将本地修复通过等同于目标机器已恢复。
