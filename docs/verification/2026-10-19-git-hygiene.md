# 提交前 Git 文件审计

日期：2026-10-19（America/New_York）。

## 范围与发现

- 更新 `origin` 后，`main` 比 `origin/main` 领先 91 个已有提交，远端没有额外提交。本次正常推送会包含这些历史提交。
- 扫描待推送历史中的 692 个 blob，以及当前跟踪和未忽略文件；检查常见 Provider/GitHub/AWS Token、私钥头和长字符串凭据赋值。未发现实际密钥；当前唯一规则命中为 `CottageLayoutTests` 使用的 `isolated-ui-test-key` 测试占位符。
- 待推送历史未发现数据库、证书私钥、可执行文件、压缩包、测试结果或日志等可疑文件名；没有超过 10 MiB 的单文件。模式扫描不能保证识别所有形式的敏感数据。
- `artifacts/` 安装包和发布目录、`.local/` 用户数据、`.dotnet-home/`、构建目录和测试结果原已忽略，没有已跟踪且匹配忽略规则的文件。
- `.superpowers/sdd/` 的 10 份本机代理执行记录不属于项目源码，移出索引并保留本地文件。过去提交中的记录仍可通过 Git 历史访问；本次不重写历史。
- `assets/snoopy/` 的原图、候选、预览及制作脚本属于动画制作资料，保留在 Git；应用实际使用资源位于 `src/DesktopPuppy/Pet/Animation/Assets/`。

## 整理

- 补充忽略 `.superpowers/`、`.env` 及变体、SQLite 数据库与旁路文件、日志与默认诊断 JSON、Python 缓存及虚拟环境；允许 `.env.example`。
- README 移除互相冲突的过期阶段说明，增加开发验证命令、目录用途和 Git 文件边界，保留历史文档链接。
- 保留并提交现有 0.1.2 界面、启动/全屏修复、打包版本和测试改动。
- 本地详细扫描结果位于被忽略的 `artifacts/git-audit/report.json`。

## 本次验证

- `dotnet build DesktopPuppy.sln -c Release --no-restore`：成功，0 警告、0 错误。
- `dotnet test tests/DesktopPuppy.Tests/DesktopPuppy.Tests.csproj -c Release --no-restore`：372 通过，0 失败、0 跳过。
- `python -m unittest discover -s assets/snoopy/tools -p 'test_*.py'`：2 通过。
- `git check-ignore`：环境文件、数据库/WAL、日志、诊断文件、代理记录和 Python 缓存样例全部匹配。
- UI/E2E 完整回归（`Category!=Performance`）：35/37 通过，2 项交互超时，0 跳过。失败分别为 `Pet_plays_all_special_sequences_and_returns_to_idle` 等待问候帧，以及 `Double_click_shows_hit_response_bubble` 等待初始窗口；结果为 `tests/DesktopPuppy.UiTests/TestResults/pre-push-ui.trx`。
- 未修改源码，单独复跑整个 `DesktopPuppyInteractionUiTests`：4/4 通过，0 失败、0 跳过，结果为 `pre-push-interaction-recheck.trx`。失败未在定向复跑中复现，仍保留交互测试存在时序或桌面环境波动的限制；不将本次完整回归记为全绿。失败后的只读全屏采样未发现抑制窗口，不能证明失败时没有环境干扰。
- 只读审查未发现严重或重要的 Git 整理问题；`git diff --check` 和暂存区检查通过。
- 本次不重跑 15 分钟性能测量，不进行安装器升级/卸载或真实 AI 请求。
