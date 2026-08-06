# 2026-08-06 远端 Text2Image 更新合并说明

## 1. 合并背景

本次合并的远端增量范围为：

- 本地合并前云端基线：`35cec7378d662929b3aa3d6ae57af015c247afff`（`Modify CSS`）
- 远端最新提交：`12f01075c6e591cf2a8a0f2631ddebebeccd107d`（`support Text2Image`）
- 对比方式：只比较远端历史版本 `35cec73..12f0107` 的新增改动；远端本次没有改动的内容继续使用本地未提交版本。

本次操作前已保存快照与留痕：

- 快照目录：`artifacts/merge-snapshots/20260806-111232/`
- 远端增量 diff：`artifacts/merge-snapshots/20260806-111232/remote-incremental-35cec73-to-12f0107.diff`
- 合并过程日志：`artifacts/merge-snapshots/20260806-111232/merge-log.md`

## 2. 远端本次新增内容

远端提交 `12f0107 support Text2Image` 的新增/修改范围如下：

- 新增 `.workbuddy/memory/2026-07-27.md`：远端工作记忆记录。
- 新增 `artifacts/carrier-product-review-report.md`：运营商产品评审报告产物。
- 新增 `skills/carrier-product-review/SKILL.md`：运营商产品评审相关 skill。
- 修改 `src/QianYuan.Api/Controllers/ImagesController.cs`：增强图片生成模型选择和 fallback 逻辑。
- 修改 `src/QianYuan.Api/appsettings.json`：图片模型列表加入 `gpt-image-1`。
- 修改 `src/QianYuan.Providers.OpenAICompat/OpenAICompatProvider.cs`：防止聊天请求误用 `gpt-image-*` 图片模型。
- 新增 `tests/QianYuan.Core.Tests/ImageGenerationTests.cs`：覆盖图片模型候选和聊天模型回退规则。

## 3. 新增功能说明

### 3.1 图片生成模型 fallback

本次合入后，图片生成请求会通过 `ImageGenerationModelResolver` 生成候选模型列表：

- 如果请求或配置使用 `gpt-image-2`，候选顺序为 `gpt-image-2`、`gpt-image-1`。
- 如果配置使用 `gpt-image-1`，候选顺序为 `gpt-image-1`、`gpt-image-2`。
- 非 `gpt-image-*` 模型保持单模型请求，不额外 fallback。

当图片服务返回与模型不支持、模型不存在、内部错误等相关的失败时，会自动尝试下一个候选模型。这样可以在 `gpt-image-2` 暂不可用或被上游拒绝时，自动降级到 `gpt-image-1`，提升 Text2Image 可用性。

相关代码：

- `src/QianYuan.Api/Controllers/ImagesController.cs` 中的 `SendGenerationRequestWithRetry`
- `src/QianYuan.Api/Controllers/ImagesController.cs` 中的 `ShouldRetryWithFallbackModel`
- `src/QianYuan.Api/Controllers/ImagesController.cs` 中的 `ImageGenerationModelResolver`

### 3.2 聊天模型保护

远端新增 `OpenAICompatModelResolver`：

- 当聊天请求误传 `gpt-image-*` 作为模型时，自动回退到 provider 的默认聊天模型。
- 普通聊天模型保持原样，不改变现有聊天调用路径。

这样可以避免图片模型被传入 Chat Completions 接口后导致请求失败。

相关代码：

- `src/QianYuan.Providers.OpenAICompat/OpenAICompatProvider.cs` 中的 `OpenAICompatModelResolver`
- `src/QianYuan.Providers.OpenAICompat/OpenAICompatProvider.cs` 中聊天请求 body 的 `model` 赋值

### 3.3 图片模型配置补充

本地原有的 `openai-image` provider 被保留，只在其 `Models` 列表中补入远端新增模型：

```json
"Models": [ "gpt-image-1", "gpt-image-2" ]
```

这不会改变默认图片模型，默认仍由本地 `ImageModel` 配置决定；只是让候选模型和前端/服务端模型枚举包含 `gpt-image-1`。

### 3.4 新增测试

远端新增测试覆盖以下规则：

- `gpt-image-2` 会生成 `gpt-image-1` fallback。
- 非图片模型不会生成额外 fallback。
- 聊天请求如果传入 `gpt-image-2`，会回退到默认聊天模型。
- 普通聊天模型会保持原样。

测试文件：`tests/QianYuan.Core.Tests/ImageGenerationTests.cs`

## 4. 是否改动了本地功能

### 4.1 本地未改动文件的功能

远端本次没有涉及的本地未提交改动，均按本地版本保留，没有用远端文件覆盖。

保留的本地功能包括但不限于：

- 多轮会话/会话持久化相关改动。
- 自定义专家、专家市场、专家团队模板相关改动。
- 技能市场、技能安装、技能启停相关改动。
- WorkTask 运行时、专家团编排和任务执行相关改动。
- Web 前端的专家市场、任务面板、技能管理、Composer、Sidebar 等本地 UI 改动。
- 数据层新增的 Conversation、CustomExpert、SkillMarket 等实体和表初始化逻辑。

### 4.2 重叠文件的处理方式

远端本次与本地未提交改动重叠的文件只有：

- `src/QianYuan.Api/Controllers/ImagesController.cs`
- `src/QianYuan.Api/appsettings.json`

处理策略不是直接使用远端覆盖，而是：

1. 先从快照恢复这两个文件的本地未提交版本。
2. 再只把远端本次新增的 Text2Image fallback 增量手工叠加进去。
3. 对重叠处进行编译和测试验证。

因此，本地在 `ImagesController.cs` 中已有的以下功能被保留：

- 独立图片 provider 选择逻辑：`ResolveImageProvider`。
- 图片 provider 判断逻辑：`IsImageProvider`。
- 提示词优化逻辑：`OptimizeImagePromptAsync`、`PromptOptimizationResult`。
- 优化后提示词回填响应字段：`OptimizedPrompt`、`PromptOptimizerProvider`、`PromptOptimizerModel`、`PromptOptimizationSkipped`、`PromptOptimizationError`。
- `gpt-image-2` image-to-image 使用 generation endpoint 和 `reference_images` 的本地适配。
- `ToDataUrl`、`CloneWithPrompt`、提示词清洗等本地辅助逻辑。

本次实际改变本地图片功能的地方只有：

- 图片请求失败时新增模型 fallback 尝试。
- 成功返回时 `Model` 会反映最终实际使用的模型；如果发生 fallback，会返回 fallback 后的模型名。
- 配置中图片 provider 的模型列表增加 `gpt-image-1`。

这些改变是增强型改动，不移除本地已有能力。

### 4.3 可能影响行为的点

- 如果上游 `gpt-image-2` 返回模型相关错误，现在会自动重试 `gpt-image-1`，因此同一次请求可能多一次上游调用。
- 如果用户在聊天中选择了 `gpt-image-*`，现在会自动改用默认聊天模型，避免聊天接口失败。
- 对普通聊天模型、普通图片模型、专家团队、技能市场、多轮会话等本地功能无直接行为改变。

## 5. 本地功能保留完整性评估

结论：本地功能保留完整性良好。

依据如下：

- 已按“远端只取本次增量、本地未改动处保持本地版本”的策略处理。
- 所有非重叠本地补丁均已重放回工作区。
- 两个重叠文件没有整体覆盖本地版本，而是手工叠加远端新增逻辑。
- 未发现 Git 冲突标记。
- 后端编译通过。
- 图片新增测试通过。
- Web 前端构建通过。

当前仍需注意：

- 本地改动仍处于未提交状态，需要后续人工审查后再 commit。
- Desktop 前端本轮未验证，因为此前本地 `src/QianYuan.Desktop/node_modules` 缺失；本次 Text2Image 增量本身没有改 Desktop 文件。
- 运行时真实图片生成依赖外部 provider/API key，编译和单元测试不能完全替代实际接口联调。

## 6. 已执行验证

已执行并通过：

```powershell
git diff --check
dotnet build --no-restore
dotnet test tests/QianYuan.Core.Tests/QianYuan.Core.Tests.csproj --no-build --filter ImageGenerationTests
cd src/QianYuan.Web
npm.cmd run build
```

验证结果：

- `git diff --check`：通过。
- `dotnet build --no-restore`：通过，仅有既有 warning。
- `ImageGenerationTests`：4 个测试全部通过。
- Web 构建：通过，仅有 Vite chunk size warning。

## 7. 当前工作区状态说明

当前 `main` 已更新到远端最新 `origin/main`：

- `HEAD`: `12f01075c6e591cf2a8a0f2631ddebebeccd107d`

当前工作区保留本地未提交功能改动，并额外包含本次手工叠加后的 Text2Image fallback 兼容。所有改动均未暂存，便于继续审查。
