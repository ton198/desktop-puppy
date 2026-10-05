# Imagegen 返回链路排查：原始结果存在，线程协议条目缺失

日期：2026-10-04。仅做排查，尚未修复 Codex / 代理的结果适配。

## 可复核证据

1. 原始会话记录的 `image_generation_call`：
   - id: `ig_08c2f8a1e3c5094b016ac2ae02667487d2b432dd30393636d3`
   - status: `completed`
   - result: 677424 个 Base64 字符，可解码为有效透明 PNG。
2. 同一轮中没有与该图像结果对应的 `image_generation_begin` / `image_generation_end` 事件，也没有 `image_gen` 的普通 function_call 记录。看到的是直接的 Responses 原生图像输出条目。
3. 使用同版本 core 临时启动 stdio app-server，仅 initialize / thread/read；没有调用 turn/start、thread/resume、模型或图像服务。读取目标轮 `01a10877-4687-7ea3-9872-680e40c4ce9a`：
   - userMessage
   - commandExecution (读取技能)
   - agentMessage
   - imageView (查看输入母版)
   - reasoning
   - commandExecution (查默认目录)
   - reasoning
   - agentMessage
   - `imageGeneration` 条目数量 = 0。
   read_thread 插件接口也缺少该条目，因此不只是这个插件的过滤问题。
4. 已安装桌面应用包版本 `26.928.3736.0`；随包核心和实际运行缓存核心均为 `codex-cli 0.159.2`，本次未发现两者版本差异。
5. 实际 feature 查询：`image_generation stable true`，不是未开启图像功能。
6. 只读查看安装包 app.asar：`.vite/build/bootstrap-DvSEn4qy.js` 中有 `imageGeneration` 渲染归一化处理，优先读取 savedPath，否则将 result 转为 `data:image/png;base64,...`。说明客户端具备显示标准图像条目的路径；本次该路径的输入条目在 app-server 返回中不存在。
7. 核心二进制包含 image-generation 扩展的保存逻辑和生成 begin/end 事件类型；但本次记录没有证明这个扩展执行过，不能因为工具呈现名称类似就认定走了同一实现路径。
8. 配置选择的 provider 为 `litellm`，协议 `responses`，base_url 为 `http://127.0.0.1:4000/v1`。这是兼容性排查线索，不是已经证明 LiteLLM 存在错误。

## 能确定的故障边界

上游原生图片输出 -> 原始 ResponseItem 存储：成功。
原始图像输出 -> app-server 的标准 imageGeneration 线程条目：缺失。
标准 imageGeneration 条目 -> 客户端 src / 卡片：代码路径存在，但本次没有收到该条目。
默认生成目录不存在：没有走通常的落盘路径；不是 PNG 解码或目录查找本身的故障证据。

因此仅修改前端图片标签或重开预览不足以修复。尚不能仅凭编译产物确定更深原因是核心对原生图像条目的处理缺口，还是自定义代理返回的事件序列 / 工具调用路径不符合核心预期；这需要可编辑源码或代理的原始流记录。

## 修复边界

没有修改已签名安装包、Codex 数据库、认证或代理配置。生成图的日志解码恢复已经完成，但不等同于工具返回链路修复。

下一步需要 LiteLLM Responses 代理的项目/部署位置，检查请求工具声明及输出 SSE 序列，尤其图像输出是否应走 Codex 注册的 image-generation 扩展。若确认流符合预期，则应在 Codex 核心事件转译/线程历史适配层增加原生 image_generation_call -> imageGeneration 的处理，并保证 live 与历史路径都覆盖。未验证前不应强行重写成 function_call，因为可能触发重复生成。

验收标准：同一调用既有有效图像数据，又能在 app-server thread/read 中看到 imageGeneration；客户端有可显示 src；助手得到可检查结果；需要落盘时文件存在；不重复生成、不覆盖已有素材。

只读诊断脚本保存在本线程 visualization 目录（imagegen-readonly-probe.py），是诊断用临时脚本，不是生产修复。

## WSL LiteLLM 部署补充检查

- 已找到运行中的 Ubuntu WSL；LiteLLM 运行于 Docker 容器 `litellm`。
- 实际安装版本：1.102.1（读取容器 Python package metadata，不依据镜像标签推断）。
- 配置宿主路径：`/opt/LiteLLM/config.yaml`，容器内 `/app/config.yaml`。
- 自定义回调：`live_routing.observer`；挂载代码 `/opt/LiteLLM/observability/live_routing.py`。静态关键词检查未发现直接引用 image_generation 或 Responses 图像 output_item 事件的逻辑；这不能证明回调对流没有其他影响。
- symphony 存在多个路由条目，其中一个经 `cliproxy:8317/v1` 转发；配置表本身不足以确定当次实际选中的路由。
- LiteLLM 已安装的 `types/responses/main.py` 有 `OutputImageGenerationCall`，type 为 image_generation_call；说明结果类型有定义，不证明所有 provider 与 SSE 序列都正确实现。
- 原始会话里实际收到有效图像结果，是本次请求并未因“完全不支持图像工具”而直接失败的更强证据。

结论：WSL 部署不是不支持工具调用的证据，LiteLLM 也不能在现有证据下被认定为根因。应对比实际上游代理输出、LiteLLM 输出和 Codex 收到的 SSE 事件，确认首次发生协议/条目转换缺失的位置；在确认前不修改路由、版本或回调。

## CLIProxyAPI 支持性检查

- 容器：cliproxy；运行二进制版本 v8.0.3、commit acdace9、builtAt 2026-09-28T00:20:22Z。`-version` 不是该程序支持的参数，本次读取的是进程启动时打印的版本横幅；查询进程退出，没有启动额外代理服务。
- 配置挂载：/opt/LiteLLM/cliproxy/config.yaml -> /CLIProxyAPI/config.yaml；config-version=8。未读取 /data/auth。
- multimedia.disable-image-generation 及旧字段都未显式设置；对照同版本官方 config.example.yaml，默认 false，即没有禁用原生 image_generation 注入。
- 同版本官方 codex_executor_request.go 有 ensureImageGenerationTool；如果没有原生 image_generation 或识别出的 image_gen.imagegen / image_gen namespace function imagegen，则可注入原生工具。检测到客户端已有对应函数工具时保持不再注入。该能力存在不证明本次具体请求触发了注入。
- 同版本配置支持 passthrough：对非 images 端点不注入、不移除原生图像工具；这只能作为控制变量测试，不能当作已验证的修复。
- 结果：此版本不是“不支持图像工具”；需要区别原生 hosted image_generation 和客户端本地函数 image_gen.imagegen。若请求工具列表在某一层丢失/改名，可能选中与客户端不匹配的执行路径；这是待验证的假设，不是已证实的根因。
- 没有改配置、重启服务、升级镜像或额外生成图片。

对照的官方版本源码：
https://raw.githubusercontent.com/router-for-me/CLIProxyAPI/v8.0.3/config.example.yaml
https://raw.githubusercontent.com/router-for-me/CLIProxyAPI/v8.0.3/internal/runtime/executor/codex_executor_request.go

## 根因收敛：本地工具未注册 + 原生结果不走展示生命周期

### 直接观察

- 本轮限时被动采样（只在 RAM 重组目标端口请求，不保存原始包或请求）：Codex -> LiteLLM 的当前线程主请求有16个工具：10 function、5 namespace、1 web_search；没有原生 image_generation，也没有 image_gen namespace / image_gen.imagegen 本地函数。另有3 function 的辅助请求，不用于推断主工具注册。
- 后续采样未可靠匹配同一线程在 LiteLLM -> CLIProxy 的请求。不能声称已完成两边 request tools 的逐项实测比较。三次采样均已自动结束。
- 生成发生时的 request tools 历史日志没有保留，本轮样本是当前配置的证据，不等同于原始生成请求快照。
- 配置条件读取：provider_id=litellm；is_openai_name=false；requires_openai_auth=false；uses_actor_authorization=false。本地 image_generation feature 为 true 并不覆盖这些条件。

### 同版本官方源码给出的具体原因

1. `codex-rs/ext/image-generation/src/extension.rs:35-43,79-100`：本地图像扩展 available 要求 provider.is_openai() 或 requires_openai_auth 或 uses_openai_actor_authorization。available 为 false 时 tools() 返回空列表。当前配置不满足任何一项，解释了进入 LiteLLM 前就没有本地函数。
2. `codex-rs/core/src/stream_events_utils.rs:409-455`：handle_non_tool_response_item 虽能识别/记录 ImageGenerationCall 的类型名，但只为 Message / Reasoning / WebSearchCall 调用 parse_turn_item；原生 ImageGenerationCall 落入 `_ => None`。因此记录原始响应成功，却没有生成开始/完成的标准图像条目。
3. `codex-rs/core/src/event_mapping.rs:229-243` 的确有原生图片到 TurnItem 的转换能力，但上述调用路径没有触发它。不能仅凭存在这个转换函数认定结果必然能呈现。
4. `codex-rs/app-server-protocol/src/protocol/thread_history.rs:594-613,839-865`：普通 ImageGeneration 生命周期项未作为 generic upsert 项；常规图像历史通过 ImageGenerationBegin / End 或 Extension ItemCompleted 路径构建。原始响应没有走这些路径，因此 thread/read 看不到图像结果。

### CLIProxyAPI 的角色与证据边界

已在本地 git 中读取正在运行版本 commit acdace9 的注入函数：缺少识别到的图像工具时可以增加 hosted image_generation；同版本官方源码有相同识别逻辑。本次实际收到 hosted 原生图像输出确定无疑，但没有历史请求快照，因此由自动注入触发属于强解释，不能冒充逐跳抓取的事实。

### 修复应落在哪里

保留现有代理链时，需要 Codex 对代理返回的原生图像结果补齐标准事件/展示/历史留存，以及安全可控的文件保存与路径回传。仅把 ImageGenerationCall 加入解析分支仍需检查历史构建和文件路径，不应当作整个问题已经修复。另一条路径是使用真正可用且有正确认证的图像执行器，完成本地工具调用流程。

不建议伪造 actor 认证头、把 provider 改名冒充 OpenAI、修改认证需求标志或直接替换签名安装文件。没有执行以上操作，也没有重启代理、修改路由或额外生成图片。

官方版本源码：
https://raw.githubusercontent.com/openai/codex/rust-v0.159.2/codex-rs/ext/image-generation/src/extension.rs
https://raw.githubusercontent.com/openai/codex/rust-v0.159.2/codex-rs/core/src/stream_events_utils.rs
https://raw.githubusercontent.com/openai/codex/rust-v0.159.2/codex-rs/core/src/event_mapping.rs
https://raw.githubusercontent.com/openai/codex/rust-v0.159.2/codex-rs/app-server-protocol/src/protocol/thread_history.rs
