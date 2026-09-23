# Desktop Puppy

一个面向个人使用的 Windows WPF 桌面宠物项目。

当前状态：Phase 3/4 进行中，已完成桌宠交互和“前台窗口 Hook → 使用会话 → 标题防抖 → SQLite”离线统计切片。

## 已确定方向

- C# + WPF + Win32 API + SQLite
- 不使用 Electron、Tauri、WebView、HTML、CSS 或 JavaScript
- 调皮捣蛋型桌宠
- 本地优先的电脑使用统计
- DeepSeek deepseek-flash 可选接入
- 混合 AI 模式：普通反馈使用本地内容，重要内容和每日总结可使用 AI
- AI 生成内容必须经过人设、重复度和格式校验
- AI 不决定桌宠行为，只负责在指定场景中生成台词

详细需求见：

- docs/superpowers/specs/2026-09-21-desktop-puppy-design.md
- docs/superpowers/plans/2026-09-21-desktop-puppy.md

## 隐私边界

默认只在本地统计应用使用时长、活跃时段和桌宠互动数据。不采集键盘内容、鼠标轨迹、屏幕截图、文档正文、网页正文或剪贴板内容。

## 当前阶段

下一步继续接入电源状态边界、日聚合与 15 天保留；完整验收范围见实现状态文档。
