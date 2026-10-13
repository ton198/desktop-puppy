# 桌宠出现与转身动画更新

- 左半屏朝右，右半屏朝左。使用桌宠所在显示器的工作区中线，缓冲距离为 16 DIP。
- 跨过缓冲边界后播放 240 毫秒转身动画。转身中反向移动，从当前姿态回转。
- 左右各 6 张展开衔接帧，维持朝向屏幕中央，逐渐回正、放下手臂。展开 350 毫秒，收起 250 毫秒；鼠标移开立即开始收起。
- 普通动作也随朝向镜像。最后衔接回现有待机素材。显示比例与图像位置随展开进度插值。
- 转身帧经过固定窗口边界检查，避免头部被裁切。

修改项目：`C:/Users/ton20/Documents/works/projects/desktop-puppy`。
新增素材：`src/DesktopPuppy/Pet/Animation/Assets/edge-reveal-{1..6}-{left,right}.png`、`turn-{1..3}.png`。
候选原图与构建脚本：`assets/snoopy/candidates/edge-reveal-v3-20261009/`。
原文件备份在本聊天工作目录 `work/before-animation-change/`。

生成方式：内置 ImageGen，以现有探头图和待机图为参考生成精灵图，随后按独立轮廓拆分、对齐和镜像。
生成提示词：Create ONE production animation sprite sheet, transparent background, exactly 4 columns by 2 rows, eight equally sized square cells, no text no grid lines. Edit/reference image 1 is start: Snoopy leaning right 45 degrees with arms bent, image 2 is exact target: upright Snoopy facing LEFT, arms relaxed. Eight full body inbetween drawings in row-major order: frame1 lean right 40deg arms bent; frame2 lean right 30deg arms slightly down; frame3 lean right 20deg arms halfway down; frame4 lean right 10deg arms relaxed facing right; frame5 upright facing right arms at sides; frame6 upright turning toward viewer three-quarter front; frame7 upright turning to LEFT three-quarter; frame8 match image2 upright facing left. Preserve Snoopy identity, thick clean black contours, white opaque body, red collar, body proportions. Same apparent body size all cells, feet baseline same in every cell, full body fits with generous margin, no cropping, no shadows. This is a turning and straightening animation for a desktop pet. Smooth anatomy change from frame to frame; retain exact face identity and ear shape. True transparent background between sprites, flat crisp ink style matching reference. Output one sheet at highest possible resolution.

## 验证

- 287 个单元测试通过，包含转身反向、屏幕中线缓冲、展开终点及 6 个转身轮廓边界检查。
- 首轮定向 UI 测试：7 项通过。
- 全部 UI 测试：20 项通过、2 项失败。失败用例：`Hiding_the_pet_hides_speech_and_exit_closes_both_windows`（未找到托盘显示菜单）；`Desktop_puppy_window_exposes_a_stable_ui_automation_surface`（未找到托盘图标）。
- 原项目定向复测：5 项通过，包含两项首次失败的托盘用例、左右贴边和跨中线转身。
- Release 构建成功，0 错误；保留原有 WFO0003 DPI 配置警告。
- 独立审查未发现新的重要问题。

预览 GIF 展示动作帧，不是实际屏幕录制。
