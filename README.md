# Desktop Puppy

一个面向个人使用的 Windows WPF 桌面宠物项目。

安装包：`artifacts/installer/DesktopPuppy-0.1.0-win-x64-Setup.exe`，支持 Windows 10 22H2 及以上 x64，内置 .NET 10 运行时，无需单独安装运行时。默认安装到当前用户的 `%LOCALAPPDATA%\Programs\DesktopPuppy`，提供开始菜单入口、可选桌面快捷方式和卸载入口；卸载保留 `%LOCALAPPDATA%\DesktopPuppy` 下的用户数据。安装包未签名。

重新打包：安装 [Inno Setup](https://jrsoftware.org/isdl.php)，执行 `pwsh -File scripts/build-installer.ps1`；可通过 `-Version 0.1.1 -IsccPath "编译器路径\ISCC.exe"` 指定版本和编译器。产物和 SHA-256 校验文件保存在 `artifacts/installer/`。

最新回归（2026-10-13）：统计修正后的最新发布版已完成完整 UI/E2E，32 项全部通过；完整单元/数据库/HTTP 套件重新运行，365 项全部通过。详见 `docs/verification/2026-10-13-post-fix-regression.md`。下文关于修正后 UI 尚未重跑的描述为历史状态，真实系统人工验收仍待完成。

当前状态（2026-10-13）：个人本地 MVP 的设置、位置记忆、启动协调、扩展统计、数据管理、问候与随机活动、日报已实现。完整单元/数据库测试 365 项通过，15 分钟空闲刷新测量通过，五个 AI 场景真实 Provider 联调通过；统计修正前的交付版 UI/E2E 共 32 项通过；修正后的 UI 暂未重跑，按用户要求不影响桌面。真实登录、全屏、锁屏、睡眠、多屏/拔屏及 Explorer 重启仍需人工验收。本轮未提交、合并或推送。

本轮更新（2026-10-08）：已实现右键 AI 台词链路、AI 设置入口、DPAPI 加密 Key、候选校验/重试、单气泡 15 秒展示期限及全屏抑制。真实 DeepSeek/Jev 联调和用户右键手动测试通过。DeepSeek 显式关闭思考，三次独立测试点击到气泡为 1.65 / 1.91 / 2.23 秒，中位数 1.91 秒；不保证每次两秒内。真实全屏及其他硬件/系统验收仍待完成。本轮合并回本地 main，不推送远端。

使用方式：运行本次交付的 `artifacts/mvp/DesktopPuppy.exe`（需要 .NET 10 Windows Desktop Runtime），或自己构建后的 `src/DesktopPuppy/bin/Release/net10.0-windows/DesktopPuppy.exe`，通过托盘 **AI 设置**，分别保存 DeepSeek 和 Jev Key，再右键桌宠。已保存的用户配置可继续使用。最多一个请求在途；占位气泡 15 秒后移除，迟到结果只记录。左键和受击继续使用本地台词。API 最终失败不使用本地备用台词。

## 已确定方向

- C# + WPF + Win32 API + SQLite
- 目标框架为 `net10.0-windows`，支持 Windows 10 22H2 及以上，优先 x64
- 不使用 Electron、Tauri、WebView、HTML、CSS 或 JavaScript
- 调皮捣蛋型桌宠：左键普通互动、双击受击、右键特殊互动、拖拽移动
- 本地优先的电脑使用统计，窗口标题稳定 2 秒后记录
- 本地历史最多保留最近 15 个本地日
- 不设置每日活动上限，也不设置每日 AI 请求上限；活动和请求仍受冷却、超时、重试及暂停规则约束
- DeepSeek 每次生成 6 个候选，候选池串行补充，至少 4 个通过硬校验后由 Jev `jev-latest` 选择
- DeepSeek 和 Jev 使用独立 API Key，并用 Windows DPAPI 加密后存入 SQLite
- AI 超时、空响应、非法 JSON、校验失败或重试耗尽时不使用本地备用台词；五次失败后显示非阻塞通知
- 日报每天本地时间 18:20 生成，截止前台使用时间超过 30 分钟才生成，并通过主题、形式和风格轮换保持新鲜感

详细需求见：

- `docs/superpowers/specs/2026-09-21-desktop-puppy-design.md`
- `docs/superpowers/plans/2026-09-21-desktop-puppy.md`

## 隐私边界

默认不采集键盘具体输入、鼠标轨迹、屏幕截图、文档正文、网页正文或剪贴板内容。应用名称和窗口标题按已确认决策允许发送给 DeepSeek/Jev；本地历史最多保留 15 个本地日。API Key 只以 DPAPI 密文形式存入 SQLite，不进入 Prompt、日志、异常信息或源代码。

## 当前阶段

已接入左右靠边探头：拖到工作区左右边缘 24 DIP 以内，松手后收起为约 45 度倾斜的半头；悬停后用 350 毫秒逐帧回正并展开，鼠标移开立即开始 250 毫秒收起。气泡显示时保持展开，半头可以直接拖走。桌宠在屏幕左半边朝右、右半边朝左，跨过所在显示器工作区中线的 16 DIP 缓冲区后播放 240 毫秒转身动画。主窗口只显示桌宠，AI 设置、统计和退出从托盘菜单访问。真实双屏/DPI和任务栏布局仍需手动验收。

使用与隐私见 [MVP 使用说明](docs/mvp-usage-and-privacy.md)。实施、自动化与待执行的人工验收见 [本次验收记录](docs/verification/2026-10-13-mvp-acceptance.md)。完整项目历史见 `docs/implementation-status.md`。
