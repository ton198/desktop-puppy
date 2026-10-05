# Imagegen 调用排查与结果恢复（2026-10-04）

## 结论

本次图像生成不是空结果：本地会话记录包含 `image_generation_call`，状态 `completed`，`result` 是完整 Base64 PNG。之前助手侧工具返回呈现为空、默认 `generated_images` 目录不存在，只能说明没有看到常规输出路径；不能据此断言生成失败。返回结果展示/文件落盘的具体故障模块仍未证实。

## 证据

- 会话：`01a10873-1466-7000-9ce5-bad9b072b515`。
- 生成事件：`ig_08c2f8a1e3c5094b016ac2ae02667487d2b432dd30393636d3`。
- 会话事件 UTC 时间：`2026-10-04T19:51:18.294Z`；纽约时间 2026-10-04 15:51:18 EDT。
- 状态：`completed`。
- `result` Base64 字符数：677424。
- 解码字节数：508067。
- Pillow 验证：有效 PNG，1254×1254，RGBA，alpha 最小0、最大255。
- SHA-256：`724032cc5ce2e2916462286b13292d6e9f3ab34795fe894db635352e085d06f6`。
- `logs_2.sqlite` 中 `codex_core::stream_events_utils` 记录收到了同一图像事件的 `output_item_done`。
- 同一线程 UTC 19:50–19:52 窗口内 WARN/ERROR 分组：`[]`。这不是对其他时间或其他日志中不存在错误的保证。
- 检查了当天桌面日志的 `image_generation`、`generated_images`、本次事件ID，无匹配记录；未发现直接指向本次调用的桌面错误。

## 已采取的操作

1. 只读打开会话日志与 SQLite（`mode=ro`），未修改 Codex 配置、认证、数据库或日志。
2. 将会话中的既有结果解码到项目的候选素材目录；没有重新调用生成服务。
3. 保存对应 revised_prompt 和精简元数据，无密钥、令牌或完整原始日志。
4. 未覆盖现有母版、动画帧或已接受素材，未接入程序。

恢复图：`C:\Users\ton20\Documents\works\projects\desktop-puppy\assets\snoopy\candidates\drag-lift-imagegen-recovered-v1.png`。
提示词：同目录 `.prompt.txt`。
元数据：同目录 `.metadata.json`。

## 后续原则

没有图片路径时，不只检查默认输出目录；优先确认会话中是否存在已完成图像事件及可验证的结果，再判断生成是否失败。当前无需重新生成或更改账号配置。具体前端渲染/结果暴露问题，如需进一步修复，要用同一事件ID检查相应客户端实现或完整诊断，而非猜测网络、额度或内容拒绝。
