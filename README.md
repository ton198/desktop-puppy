# Desktop Puppy

一个面向个人使用的 Windows WPF 桌面宠物项目。

当前状态：需求、架构和实现计划阶段，尚未开始实现产品代码。

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

请先审阅需求说明和实施计划。批准后再按照计划创建 WPF 工程和产品代码。
