# 第一阶段专家功能实现说明

本文档说明本次按照《QianYuan短期目标规划》第一阶段要求完成的专家系统改造内容，包括后端 API、数据模型、前端交互、专家 Prompt 本地化、Agent 绑定以及品牌替换范围。

## 一、实现目标

第一阶段目标聚焦“专家系统本地化增强”，本次实现覆盖以下能力：

- 官方专家目录继续可浏览、检索、查看详情、获取系统提示词。
- 新增用户自定义专家的创建、编辑、删除、列表、详情能力。
- 自定义专家支持 `systemPrompt`、`tags`、`categoryId`、`quickPrompts`、`author`、`avatarUrl` 等字段。
- 自定义专家支持绑定 Agent Store 中的 Agent。
- 用户从专家市场召唤专家时，会自动使用专家系统提示词，并在有绑定 Agent 时自动切换到该 Agent。
- 聊天接口支持 Agent Store Agent 的系统提示词、默认 Provider/Model、挂载 Skills 与专家提示词合并使用。
- 官方专家 Prompt 支持优先本地缓存读取，缺失时远程拉取并落盘缓存。
- Expert data and copy use `QIANYUAN` / `qianyuan` instead of the prohibited original brand terms.

## 二、后端数据层改动

### `src/QianYuan.Data/Entities/CustomExpert.cs`

新增 `CustomExpert` 实体，用于保存用户自定义专家：

- `Id`：专家唯一 ID，使用字符串，兼容前端自定义 slug。
- `UserId`：所属用户 ID，实现用户隔离。
- `CategoryId`：专家分类，默认 `custom`。
- `Name` / `Profession` / `Description`：专家基础信息。
- `AvatarUrl`：头像 URL。
- `SystemPrompt`：专家系统提示词。
- `TagsJson`：标签数组 JSON。
- `QuickPromptsJson`：快捷提示词数组 JSON。
- `BoundAgentId`：绑定的 Agent Store Agent ID。
- `Author`：作者信息。
- `Enabled`：软删除/启用标记。
- `CreatedAt` / `UpdatedAt`：审计时间。

### `src/QianYuan.Data/QianYuanDbContext.cs`

新增 `DbSet<CustomExpert>` 并配置 EF Core 映射：

- 主键：`CustomExpert.Id`。
- 外键：`CustomExpert.UserId -> UserAccount.Id`。
- 索引：`UserId + Name`，加速用户维度查询。
- 字段长度：`Id` 256、`CategoryId` 80、`Name` 120、`Profession` 160、`AvatarUrl` 1000、`BoundAgentId` 256。

### `src/QianYuan.Data/DataServiceCollectionExtensions.cs`

新增数据库初始化补表逻辑：

- 在 `InitializeDatabaseAsync` 中调用 `EnsureCustomExpertTablesAsync`。
- `EnsureCustomExpertTablesAsync` 支持三类数据库：
  - SQLite：`CREATE TABLE IF NOT EXISTS "CustomExperts"`。
  - PostgreSQL：`CREATE TABLE IF NOT EXISTS "CustomExperts"`。
  - SQL Server：`IF OBJECT_ID(N'[CustomExperts]', N'U') IS NULL CREATE TABLE [CustomExperts]`。
- 三类数据库均创建 `IX_CustomExperts_UserId_Name` 索引。

## 三、后端模型与服务改动

### `src/QianYuan.Api/Models/ExpertCatalogModels.cs`

扩展专家 DTO：

- `ExpertSummaryDto` 新增：
  - `IsCustom`：是否自定义专家。
  - `BoundAgentId`：绑定的 Agent ID。
- `ExpertDetailDto` 同步新增：
  - `IsCustom`。
  - `BoundAgentId`。

新增请求/响应模型：

- `CustomExpertUpsertRequest`：创建/编辑自定义专家请求。
- `ExpertBindAgentRequest`：绑定 Agent 请求。
- `ExpertPromptDto`：返回专家系统提示词和绑定 Agent。
- `ExpertChatRequest`：专家对话请求。
- `ExpertChatResponse`：专家对话响应。

### `src/QianYuan.Api/Services/CustomExpertService.cs`

新增自定义专家服务 `ICustomExpertService` / `CustomExpertService`，实现：

- `ListAsync`：按当前用户列出自定义专家，支持分类、类型、关键字、标签、作者过滤。
- `GetAsync`：获取当前用户自定义专家详情。
- `GetPromptAsync`：获取当前用户自定义专家系统提示词。
- `CreateAsync`：创建自定义专家。
- `UpdateAsync`：编辑自定义专家。
- `BindAgentAsync`：更新自定义专家绑定的 Agent。
- `DeleteAsync`：软删除自定义专家。

关键实现细节：

- 自定义专家使用用户隔离，所有查询都要求 `UserId` 匹配。
- 删除采用 `Enabled = false`，避免直接物理删除。
- `tags` 和 `quickPrompts` 使用 JSON 字符串存储，减少迁移复杂度。
- 创建时会标准化 ID，并避免与官方专家 ID 或已有自定义专家 ID 冲突。
- 默认 `defaultInitPrompt` 使用第一条 quick prompt；如果没有 quick prompt，则生成兜底提示词。

### `src/QianYuan.Api/Services/ExpertCatalogService.cs`

重写/增强官方专家目录服务：

- 保留官方专家目录加载、分类、场景、列表、详情能力。
- `ExpertSummaryDto` / `ExpertDetailDto` 映射新增 `IsCustom = false` 和 `BoundAgentId`。
- Prompt 读取逻辑增强：
  - 优先读取本地路径 `Data/experts/prompts/...`。
  - 本地不存在时从原始资源 URL 拉取。
  - 拉取成功后写入本地缓存目录。
  - 拉取失败时使用 fallback persona。
- 新增路径安全处理，防止 prompt 路径穿越。
- fallback persona 使用中文专业身份提示。

### `src/QianYuan.Api/Program.cs`

新增服务注册：

- `builder.Services.AddScoped<ICustomExpertService, CustomExpertService>();`

## 四、后端控制器改动

### `src/QianYuan.Api/Controllers/ExpertsController.cs`

扩展专家 API：

#### 官方/合并查询

- `GET /api/experts/categories`
  - 返回官方专家分类。
- `GET /api/experts/scenarios`
  - 返回精选场景。
- `GET /api/experts`
  - 合并返回官方专家和当前用户自定义专家。
  - 支持 query：
    - `category`
    - `type`
    - `q`
    - `sort`
    - `tag`
    - `author`
    - `isCustom`
- `GET /api/experts/{id}`
  - 优先查当前用户自定义专家；不存在再查官方专家。
- `GET /api/experts/{id}/prompt`
  - 返回 `systemPrompt` 和 `boundAgentId`。

#### 自定义专家 CRUD

- `POST /api/experts/custom`
  - 创建自定义专家。
  - 需要登录。
- `PUT /api/experts/custom/{id}`
  - 编辑自定义专家。
  - 需要登录。
- `PUT /api/experts/custom/{id}/agent`
  - 单独更新绑定 Agent。
  - 需要登录。
- `DELETE /api/experts/custom/{id}`
  - 删除自定义专家。
  - 需要登录。

#### 专家对话

- `POST /api/experts/{id}/chat`
  - 支持直接通过专家 ID 发起对话。
  - 若专家绑定 Agent，则通过 `IAgentExecutionService.InteractAsync` 调用绑定 Agent。
  - 若未绑定 Agent，则使用默认或指定 Provider/Model 直接调用 LLM Provider。
  - 响应包含完整 `content` 和 `chunks`。

### `src/QianYuan.Api/Controllers/ChatController.cs`

增强主聊天流对 Agent Store Agent 的支持：

- 当 `req.AgentId` 不是内置 registry Agent 时，尝试从 `IAgentRepository` 读取 Agent Store Agent。
- 找到启用的 Agent Store Agent 后：
  - 使用默认内置 `qianyuan.default` 执行器承载对话。
  - 合并 Agent Store Agent 的 `SystemPrompt` 和专家 `SystemPrompt`。
  - 使用 Agent Store Agent 的默认 Provider/Model，除非请求显式覆盖。
  - 将 Agent Store Agent 挂载的 Skills 加入 `PreloadSkills`。
- 新增辅助方法：
  - `BuildSystemPromptOverride`
  - `BuildPreloadSkills`

## 五、前端类型与 API 封装改动

### `src/QianYuan.Web/src/types/api.ts`

扩展前端类型：

- `ExpertSummaryDto` / `ExpertDetailDto` 新增：
  - `isCustom`
  - `boundAgentId`
- 新增：
  - `CustomExpertUpsertRequest`
  - `ExpertChatRequest`
  - `ExpertChatResponse`
- `ExpertPromptDto` 新增 `boundAgentId`。

### `src/QianYuan.Web/src/services/api.ts`

扩展专家 API 调用封装：

- `listExperts` 增加 `tag`、`author`、`isCustom` 参数，并改用 `apiFetch`，支持登录态读取自定义专家。
- `getExpert` 改用 `apiFetch`，支持读取当前用户自定义专家详情。
- `getExpertPrompt` 改用 `apiFetch` 并支持 `boundAgentId`。
- 新增：
  - `createCustomExpert`
  - `updateCustomExpert`
  - `bindCustomExpertAgent`
  - `deleteCustomExpert`
  - `chatWithExpert`

## 六、前端页面改动

### `src/QianYuan.Web/src/App.tsx`

增强专家召唤逻辑：

- `ActiveExpert` 新增 `boundAgentId`。
- `summonExpert` 中：
  - 进入聊天页。
  - 将当前 Agent 设置为专家的 `boundAgentId`。
  - 获取专家 prompt 后再次同步 `boundAgentId`。
  - 将专家系统提示词传给主聊天流。
- 如果专家没有绑定 Agent，保持原来的纯系统提示词模式。

### `src/QianYuan.Web/src/components/ExpertMarketplace.tsx`

重写专家市场组件，新增完整自定义专家体验：

- 顶部新增搜索框。
- 新增“我的专家”筛选按钮。
- 新增“新建专家”按钮。
- 专家列表支持显示：
  - 专家团 badge。
  - OPC badge。
  - 自定义 badge。
- 专家详情中显示：
  - 分类。
  - 作者。
  - 绑定 Agent。
  - tags。
  - quick prompts。
- 自定义专家详情支持：
  - 编辑。
  - 删除。
  - 召唤。
- 新增自定义专家表单：
  - ID（创建时可选）。
  - 名称。
  - 职业/定位。
  - 分类。
  - 绑定 Agent。
  - 头像 URL。
  - 作者。
  - 标签。
  - 描述。
  - 系统提示词。
  - Quick Prompts。
- 表单保存后会刷新列表，并打开保存后的专家详情。

### `src/QianYuan.Web/src/styles.css`

新增/扩展样式：

- `.market-actions`
- `.market-mine.active`
- `.market-mine.primary`
- `.ec-badge.custom`
- `.ed-actions`
- `.ed-secondary`
- `.ed-danger`
- `.custom-expert-form`
- `.custom-form-grid`
- 暗色主题适配。
- 小屏幕响应式布局。

## 七、专家 Manifest 品牌替换

### `src/QianYuan.Api/Data/experts/expert-manifest.json`

完成专家 manifest 中品牌替换：

- Original Chinese brand terms were replaced with `QIANYUAN`.
- Original English brand terms were replaced with `QIANYUAN`.
- Original lowercase brand terms were replaced with `qianyuan`.

验证命令：

```powershell
rg -n "<forbidden-brand-pattern>" src docs README.md skills -S -g '!**/bin/**' -g '!**/obj/**'
```

结果：无匹配。

## 八、验证结果

### 后端构建

执行命令：

```powershell
dotnet build QianYuan.AgenticFramework.sln --no-restore
```

结果：构建成功。

保留警告：

- `SQLitePCLRaw.lib.e_sqlite3` 已知高危漏洞警告，属于现有依赖版本问题。
- `System.Text.Encoding.CodePages` pruning 建议。
- 少量现有 nullable warning。

### 前端构建

执行命令：

```powershell
npm run build
```

结果：构建成功。

保留警告：

- Vite chunk size 超过 500KB 的提示，属于打包体积建议，不影响功能。

## 九、使用方式

### 创建自定义专家

1. 登录系统。
2. 打开专家市场。
3. 点击“新建专家”。
4. 填写专家名称、定位、描述、系统提示词。
5. 可选绑定 Agent Store 中的 Agent。
6. 保存后可在“我的专家”中筛选查看。

### 召唤自定义专家

1. 打开自定义专家详情。
2. 点击 quick prompt 或“召唤专家”。
3. 系统会自动：
   - 将专家系统提示词注入聊天。
   - 如果绑定了 Agent，则切换到绑定 Agent。
   - 如果绑定 Agent 有 Provider/Model/Skills，会在主聊天流中生效。

### API 示例

创建自定义专家：

```http
POST /api/experts/custom
Content-Type: application/json
Authorization: Bearer <token>

{
  "name": "增长策略专家",
  "profession": "增长策略顾问",
  "description": "擅长从用户、渠道、内容、转化漏斗角度制定增长策略。",
  "systemPrompt": "你是一名增长策略专家，请输出结构化、可执行的增长建议。",
  "categoryId": "custom",
  "tags": ["增长", "运营", "策略"],
  "quickPrompts": ["帮我诊断当前增长瓶颈", "为新产品设计增长实验"],
  "boundAgentId": "my-growth-agent"
}
```

获取专家 prompt：

```http
GET /api/experts/{id}/prompt
```

专家对话：

```http
POST /api/experts/{id}/chat
Content-Type: application/json

{
  "message": "请帮我制定一个两周增长实验计划。"
}
```

## 十、注意事项

- 自定义专家目前按用户隔离，未登录用户只能看到官方专家。
- 删除自定义专家为软删除，不会立即物理删除数据库记录。
- 官方专家 prompt 本地缓存目录为 `Data/experts/prompts`，首次缺失会尝试从资源 URL 拉取。
- 绑定 Agent Store Agent 时，主聊天流仍由默认内置 ReAct Agent 承载，但会合并绑定 Agent 的系统提示词、Provider/Model 和 Skills。
- 本次不包含第二阶段专家团模板、并行执行和 SSE 实时执行日志能力。
