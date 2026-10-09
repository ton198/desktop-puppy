# 左右靠边探头素材候选 v1 — 2026-10-08

状态：素材阶段，等待用户视觉确认；尚未接入 WPF 功能。原有运行资源未修改。

使用内置 ImageGen，现有 `idle.png` 为角色参考。三次生成的完整提示词见 `prompts.json`。原始 PNG 保留在 `*.raw.png`，左侧源素材朝右，右侧由水平镜像制作。

- `peek-left/right.png`：抱爪偷看。
- `transition-left/right.png`：收身过渡。
- `blink-left/right.png`：探头眨眼。
- `contact-sheet-light/dark.png`：完整姿势及半头裁切对照。
- `edge-peek-preview.gif`：完整、收起、眨眼、悬停展开的双侧效果示意；不是程序运行验收。

全部透明 PNG 为 1254×1254，保持生成画布，不逐帧紧裁。预览以源图 x=700 为左侧遮挡线，头部可见宽度约 43%，身体和爪子在边界外。右侧镜像同一几何关系。比例估算依据人工头部参考范围，不是语义分割。

预览每次滑动 250ms；停靠时眨眼120ms。动画展开最后回到现有 idle 母版（左侧镜像），可观察新旧素材衔接。生成帧存在微小轮廓差异，不声称眼部之外逐像素一致。实际窗口按钮、气泡、DPI、双屏裁切及鼠标行为尚未实现或验收。

`build_preview.py` 只生成镜像素材、背景对照、GIF 与验证记录，不修改原始生成图。使用 Pillow，重建命令：

```powershell
& 'C:/Program Files/Python311/python.exe' assets/snoopy/candidates/edge-peek-v1-20261008/build_preview.py
```

用户确认视觉后，按聊天中已批准的左右靠边计划接入状态控制器及现有定位、动画与互动逻辑。
