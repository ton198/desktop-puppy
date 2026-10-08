# Desktop Puppy

一个面向个人使用的 Windows WPF 桌面宠物项目。

当前状态（2026-10-07）：Phase 4 进行中。已完成桌宠交互、Snoopy 动画接入、SQLite 离线使用统计与统计窗口、15 日保留策略、Windows 锁屏/挂起/恢复事件接入、显示器/DPI 放置和托盘。最近验证：214 个单元测试、12 个桌面 UI 测试通过。

本 worktree 更新（2026-10-08）：已实现右键 AI 台词链路、AI 设置入口、DPAPI 加密 Key、候选校验/重试、单气泡 15 秒展示期限及全屏抑制。通过 260 个单元测试和 14 个桌面 UI 测试；真实在线 API 和硬件/系统验收尚未运行。改动保留在独立 worktree，尚未合并或推送。

使用方式：运行此 worktree 的 Release 程序，通过桌宠上的 **AI** 按钮或托盘 **AI 设置**，分别保存 DeepSeek 和 Jev Key，再右键桌宠。最多一个请求在途；占位气泡 15 秒后移除，迟到结果只记录。左键和受击继续使用本地台词。API 最终失败不使用本地备用台词。

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

下一步：配置真实 Key 完成右键在线联调，检查程序内动画和真实全屏行为，完成托盘、锁屏/睡眠、多显示器/DPI 与双实例验收；后续扩展问候/随机活动 AI、日报、完整设置持久化与开机启动。完整记录见 `docs/implementation-status.md`。
