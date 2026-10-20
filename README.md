# Desktop Puppy

一个面向个人使用的 Windows WPF 桌面宠物项目。

安装包：`artifacts/installer/DesktopPuppy-0.1.2-win-x64-Setup.exe`，支持 Windows 10 22H2 及以上 x64，内置 .NET 10 运行时，无需单独安装运行时。默认安装到当前用户的 `%LOCALAPPDATA%\Programs\DesktopPuppy`，提供开始菜单入口、可选桌面快捷方式和卸载入口；卸载保留 `%LOCALAPPDATA%\DesktopPuppy` 下的用户数据。安装包未签名。

重新打包：安装 [Inno Setup](https://jrsoftware.org/isdl.php)，执行 `pwsh -File scripts/build-installer.ps1`；可通过 `-Version 0.1.2 -IsccPath "编译器路径\ISCC.exe"` 指定版本和编译器。产物和 SHA-256 校验文件保存在 `artifacts/installer/`。

0.1.2 更新（2026-10-18）：统一设置、统计、日报、气泡和托盘菜单的奶油色界面，精简界面文案，并保留 0.1.1 的启动与全屏修复。界面版本已完成 372 项单元及 37 项 UI/E2E 回归；安装包交付与校验记录见 `docs/verification/2026-10-18-release-0.1.2.md`。

当前状态：0.1.2 已实现个人本地 MVP 的设置、位置记忆、启动协调、统计与数据管理、问候与随机活动及日报。自动化验证记录见 [0.1.2 交付记录](docs/verification/2026-10-18-release-0.1.2.md)；真实登录、锁屏、睡眠、多屏/DPI 切换及升级/卸载仍需实机验收。历史实施过程见 [项目历史](docs/implementation-status.md)。

使用方式：安装本地构建的安装包，或构建后运行 `src/DesktopPuppy/bin/Release/net10.0-windows/DesktopPuppy.exe`。从托盘打开“设置”的 AI 页面，分别保存 DeepSeek 和 Jev Key，再右键桌宠。左键和受击使用本地台词；API 最终失败不使用本地备用台词。

## 开发与验证

需要 Windows 10 22H2 或以上，以及 .NET 10 SDK（版本策略见 `global.json`）。从项目根目录执行：

```powershell
dotnet restore DesktopPuppy.sln
dotnet build DesktopPuppy.sln -c Release --no-restore
dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj -c Release --no-restore
$env:DESKTOP_PUPPY_EXE = Join-Path $PWD 'src/DesktopPuppy/bin/Release/net10.0-windows/DesktopPuppy.exe'
dotnet test tests/DesktopPuppy.UiTests/DesktopPuppy.UiTests.csproj -c Release --no-restore --filter 'Category!=Performance'
```

UI/E2E 测试需要可交互的 Windows 桌面，会移动鼠标并打开测试窗口；使用隔离数据库。`Performance` 类别包含 15 分钟性能测量，按需单独运行。

## 项目目录与 Git 边界

- `src/`：WPF 应用及运行时动画资源。
- `tests/`：单元、数据库、HTTP、UI/E2E 和 Windows 人工验收工具。
- `assets/`：动画原图、候选素材、制作脚本与预览，保留用于复现和维护。
- `docs/`：需求、计划、使用与隐私说明、验收记录。
- `scripts/`、`packaging/`：诊断和安装包构建脚本。
- `artifacts/`、`bin/`、`obj/`、`TestResults/`、`.local/`、`.dotnet-home/`、`.superpowers/`：本地产物或工具状态，不提交。

API Key、`.env`、私钥证书、使用数据库与诊断日志不进入 Git；安装包位于被忽略的 `artifacts/installer/`，克隆仓库后需要自行构建。提交前审计见 [Git 文件审计](docs/verification/2026-10-19-git-hygiene.md)。

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

已接入左右靠边探头：拖到工作区左右边缘 24 DIP 以内，松手后收起为约 45 度倾斜的半头；悬停后用 350 毫秒逐帧回正并展开，鼠标移开立即开始 250 毫秒收起。气泡显示时保持展开，半头可以直接拖走。桌宠在屏幕左半边朝右、右半边朝左，跨过所在显示器工作区中线的 16 DIP 缓冲区后播放 240 毫秒转身动画。主窗口只显示桌宠，设置、统计和退出从托盘菜单访问。真实双屏/DPI和任务栏布局仍需手动验收。

使用与隐私见 [MVP 使用说明](docs/mvp-usage-and-privacy.md)。实施、自动化与待执行的人工验收见 [本次验收记录](docs/verification/2026-10-13-mvp-acceptance.md)。完整项目历史见 `docs/implementation-status.md`。
