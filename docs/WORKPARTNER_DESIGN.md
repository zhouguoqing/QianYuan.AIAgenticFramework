# WorkPartner 产品与技术设计

> 基于 QianYuan.AgenticFramework 打造对标 WorkBuddy 的全场景 AI 工作台。

## 1. 背景与目标

WorkPartner 的目标不是再做一个聊天机器人，而是做一个可交付成果的 AI 工作台：用户用自然语言提出目标，系统自动规划任务、调度专家团、多 Agent 协同执行，并把最终结果沉淀为文档、代码、报告、PPT、数据分析、文件变更或可预览产物。

参考 WorkBuddy 的产品能力，WorkPartner 需要支持：

- Mac / Windows 桌面端，安装即用。
- 专家团：运营、设计、开发、测试、数据、法务、财务、产品等虚拟岗位。
- 多 Agent 协同：串行流水线、并行专家会审、主控 Agent 调度。
- Skills / MCP / CLI 扩展能力。
- 多模型切换与 Auto 模型调度。
- 用户注册、登录、JWT 鉴权。
- Credits 积分、套餐、用量、账单管理。
- 多操作系统部署：桌面端、本地服务器、企业私有化、未来 IM / 小程序接入。
- 专业、细致、面向办公交付的三栏工作台界面。

## 2. WorkBuddy 产品拆解

WorkBuddy 的核心表达是：`一句话指令 -> 自主规划 -> 多专家执行 -> 完整交付`。

### 2.1 关键卖点

- AI 专家团：100+ 领域专家组成虚拟团队。
- 全场景办公：外部信息调研、内容生成、数据洞察、文件处理、PPT/报告生成。
- 多专家并行协作：一个人顶一支团队。
- MCP 生态 + 自定义 Skills：能力可扩展。
- 全平台：桌面、主流 IM、小程序、移动端。
- 免部署：面向普通用户的安装即用体验。

### 2.2 典型界面模型

WorkBuddy 的工作台是典型三栏结构：

- 左栏：任务、助理、项目、专家、技能、连接器、知识库、设置入口。
- 中栏：任务对话和执行过程，支持追问、上传文件、查看步骤。
- 右栏：结果区，包含产物、文件列表、变更、预览。

这比传统 Chat UI 更接近办公生产工具。WorkPartner 应采用三栏工作台，而不是仅扩展当前二栏聊天界面。

### 2.3 账户与计费参考

WorkBuddy 使用积分 Credits 和会员套餐控制权益：

- 免费体验额度。
- 月度基础积分 + 赠送积分。
- 自动任务数量限制。
- 个人助理数量限制。
- 项目数量与协作成员限制。
- Auto 模型调度 / 全模型可选差异。

WorkPartner 应将 Credits 设计成通用计量层，而不是只统计 Token。

## 3. 现有 QianYuan 能力盘点

QianYuan.AgenticFramework 已经具备 WorkPartner 的核心技术底座：

- .NET 10 后端，分层清晰。
- ReAct 引擎与 LoopEngineering。
- 渐进式 Skill 加载。
- 多模型 Provider：OpenAI Compatible、Azure OpenAI、Anthropic、Gemini、Qwen Native 等。
- MCP Client / Server。
- Agent Registry 与 Agent-as-Tool。
- Agent Store：可视化创建、编辑、编排企业智能体，挂载 Skills / MCP / CLI。
- React WebUI：聊天、Agent Store、Skills Manager、Knowledge Manager。
- SSE / SignalR 流式输出。
- DingTalk 集成。
- QianYuan.Data 数据层，当前支持 SQLite / SQL Server 检测和 Agent 持久化。

主要缺口：

- 用户体系、鉴权与租户隔离。
- Credits 与商业化计费。
- 专家团编排器和任务级产物管理。
- 三栏工作台 UI。
- Electron 桌面壳。
- PostgreSQL / MySQL 支持。

## 4. 产品定位

### 4.1 一句话定位

WorkPartner 是一个面向个人、团队和企业私有化场景的 AI 专家团办公工作台，能够从自然语言目标出发，自动规划、协同执行并交付可验收成果。

### 4.2 核心用户

- 个体创业者、自由职业者、小微团队负责人。
- 研发团队：产品、架构、开发、测试、运维。
- 运营、市场、销售、客户成功团队。
- 企业内部知识工作者。
- 有私有化和多模型需求的组织。

### 4.3 MVP 场景

第一版不要追求所有办公场景，建议聚焦 4 个高价值闭环：

1. 研发任务交付：需求理解、概要设计、代码生成、测试建议、变更总结。
2. 深度调研报告：联网检索、资料归纳、结构化报告。
3. 数据分析报告：上传表格、分析指标、生成图表解释和行动建议。
4. 文件批处理：授权目录、批量重命名、整理、转换、摘要。

## 5. 总体架构

```text
Electron Desktop (Mac / Windows)
  ├─ Main Process
  │   ├─ 启动/停止本地 QianYuan.Api
  │   ├─ 自动更新、托盘、窗口、系统权限
  │   └─ 本地文件选择、目录授权、安全 IPC
  └─ Renderer: WorkPartner Web UI (React + Vite)
      ├─ 三栏工作台
      ├─ 登录/注册/Credits/套餐
      ├─ 专家团/任务/结果/文件预览
      └─ SSE/SignalR 与 API 通信

QianYuan.Api (.NET 10)
  ├─ AuthController / AccountController
  ├─ CreditsController / BillingController
  ├─ WorkTasksController / ArtifactsController
  ├─ ExpertTeamsController / ExpertsController
  ├─ AgentsController / AgentStoreController (existing)
  ├─ SkillsController / KnowledgeController (existing)
  └─ ChatController / ImagesController / McpServerController (existing)

QianYuan.Kernel
  ├─ ReActEngine (existing)
  ├─ SkillManager (existing)
  ├─ LlmProviderRegistry (existing)
  ├─ AgentRegistry (existing)
  └─ ExpertTeamOrchestrator (new)

QianYuan.Data
  ├─ EF Core DbContext
  ├─ PostgreSQL / MySQL provider support
  ├─ Accounts / Credits / WorkTasks / Artifacts entities
  └─ Existing Agent Store entities
```

## 6. 后端模块设计

### 6.1 QianYuan.Accounts

新增账户模块，可放在 `src/QianYuan.Api` 内起步，稳定后拆为 `src/QianYuan.Accounts`。

职责：

- 用户注册。
- 登录和刷新 Token。
- 密码哈希。
- JWT 生成与校验。
- 用户资料。
- 角色与权限。
- 可选：邮箱验证、找回密码、OAuth。

核心实体：

```csharp
public sealed class UserAccount
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Status { get; set; } = "Active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
```

API：

- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/refresh`
- `POST /api/auth/logout`
- `GET /api/account/me`
- `PATCH /api/account/me`

### 6.2 Credits 模块

Credits 不只代表 Token，而是统一的资源计量单位。

消耗来源：

- LLM 输入/输出 Token。
- 图像生成。
- 深度研究任务。
- 文件处理任务。
- 专家团多 Agent 调度。
- 高级模型倍率。

核心实体：

```csharp
public sealed class CreditWallet
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public long Balance { get; set; }
    public long MonthlyQuota { get; set; }
    public DateOnly QuotaMonth { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CreditTransaction
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; } = "Consume";
    public long Amount { get; set; }
    public long BalanceAfter { get; set; }
    public string SourceType { get; set; } = "";
    public string? SourceId { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SubscriptionPlan
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public long MonthlyCredits { get; set; }
    public int MaxAssistants { get; set; }
    public int MaxProjects { get; set; }
    public int MaxAutoTasks { get; set; }
    public bool AllowAllModels { get; set; }
}
```

API：

- `GET /api/credits/wallet`
- `GET /api/credits/transactions?cursor=&take=`
- `POST /api/credits/estimate`
- `GET /api/plans`
- `GET /api/billing/invoices`
- `GET /api/usage/summary?month=`

第一版可只做内部账本，不接支付。后续再接微信/支付宝/Stripe 或企业采购。

### 6.3 Expert Team 模块

专家本质上是带角色、能力、工具边界和协作协议的 Agent。

现有 Agent Store 可以作为专家资产库，新增 Team 编排层即可。

核心概念：

- Expert：专家角色，引用一个 Agent Definition。
- ExpertTeam：专家团模板。
- Workflow：协作流程，可串行/并行/评审。
- WorkTask：一次用户任务。
- WorkStep：任务中的一个执行步骤。
- Artifact：任务交付产物。

典型研发团队模板：

```text
User Goal
  -> Coordinator 主控专家
  -> ProductExpert 澄清需求
  -> ArchitectExpert 输出设计与拆分
  -> DeveloperExpert 实现或生成代码
  -> QaExpert 验证与风险检查
  -> Coordinator 汇总交付
```

并行调研模板：

```text
User Topic
  -> Coordinator 拆分子问题
  -> MarketExpert / TechExpert / CompetitorExpert 并行检索
  -> AnalystExpert 交叉归纳
  -> WriterExpert 生成报告
```

新增服务：

- `IExpertTeamService`
- `IExpertTeamOrchestrator`
- `IWorkTaskService`
- `IArtifactService`

API：

- `GET /api/experts`
- `POST /api/experts`
- `GET /api/expert-teams`
- `POST /api/expert-teams`
- `POST /api/work-tasks`
- `GET /api/work-tasks`
- `GET /api/work-tasks/{id}`
- `POST /api/work-tasks/{id}/stream`
- `GET /api/work-tasks/{id}/artifacts`
- `GET /api/artifacts/{id}`

### 6.4 Task 与 Artifact 模型

WorkPartner 要突出“交付”，所以需要任务和产物，不应只把内容存在聊天消息里。

```csharp
public sealed class WorkTask
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = "";
    public string Goal { get; set; } = "";
    public string Status { get; set; } = "Draft";
    public string? TeamId { get; set; }
    public string? ProviderId { get; set; }
    public string? Model { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class WorkArtifact
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "text/markdown";
    public string StorageKind { get; set; } = "Database";
    public string? Content { get; set; }
    public string? FilePath { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

右侧结果区直接读取 Artifact，而不是解析聊天记录。

## 7. 数据库设计

用户已确认使用 PostgreSQL / MySQL。

当前 `QianYuan.Data` 自动检测 SQLite / SQL Server，需要扩展：

- PostgreSQL：`Npgsql.EntityFrameworkCore.PostgreSQL`
- MySQL：`Pomelo.EntityFrameworkCore.MySql`
- 配置项：`Database:Provider = PostgreSQL | MySQL | Sqlite | SqlServer`

建议不要继续只靠连接字符串猜测数据库类型，改为显式配置优先、连接字符串检测兜底。

建议连接配置：

```json
{
  "Database": {
    "Provider": "PostgreSQL",
    "ConnectionStringName": "QianYuanDb"
  },
  "ConnectionStrings": {
    "QianYuanDb": "Host=localhost;Port=5432;Database=workpartner;Username=workpartner;Password=..."
  }
}
```

核心表：

- `users`
- `refresh_tokens`
- `credit_wallets`
- `credit_transactions`
- `subscription_plans`
- `user_subscriptions`
- `work_tasks`
- `work_steps`
- `work_artifacts`
- `expert_teams`
- `expert_team_members`
- `expert_workflows`
- 现有 Agent Store 表继续复用。

多租户可后置，但所有新表应保留 `UserId`，未来可增加 `TenantId`。

## 8. 前端设计

现有 React WebUI 可以保留 `useChat`、`api.ts`、Markdown 渲染、Agent Store 和 Skills Manager 的大量代码，但整体信息架构需要从“聊天工具”升级为“任务工作台”。

### 8.1 新页面结构

```text
WorkPartnerApp
  ├─ AuthLayout
  │   ├─ LoginPage
  │   └─ RegisterPage
  └─ WorkspaceLayout
      ├─ AppSidebar
      │   ├─ NewTaskButton
      │   ├─ TaskList
      │   ├─ ExpertsEntry
      │   ├─ SkillsEntry
      │   ├─ KnowledgeEntry
      │   ├─ ModelsEntry
      │   └─ AccountEntry
      ├─ TaskConversationPanel
      │   ├─ TaskHeader
      │   ├─ ExecutionTimeline
      │   ├─ MessageList
      │   └─ Composer
      └─ ResultPanel
          ├─ ArtifactTabs
          ├─ FilesView
          ├─ ChangesView
          └─ PreviewView
```

### 8.2 视觉方向

界面风格应是专业办公软件，而不是营销页或单纯聊天页：

- 三栏密集但有呼吸感。
- 低饱和深色主题 + 可选浅色主题。
- 信息层级清晰：任务、步骤、专家、产物、Credits 都可扫描。
- 大量状态：运行中、等待授权、失败、已完成、扣费估算、模型倍率。
- 图标按钮优先，复杂操作加 tooltip。
- 右侧结果区是核心，不是附属面板。

推荐后续引入：

- `lucide-react`：统一图标。
- `@tanstack/react-query`：复杂数据请求和缓存。
- `zustand`：轻量全局状态。
- 是否引入 UI 库需要谨慎。若追求完全定制，可继续 CSS 变量 + 自研组件。

### 8.3 关键 UI 模块

- 登录/注册页。
- AccountPopover：用户资料、套餐、Credits 余额。
- CreditsDashboard：余额、月度额度、交易流水。
- NewTaskModal：目标、任务类型、专家团、模型、附件。
- ExpertTeamBuilder：专家成员、角色、顺序、并行节点。
- TaskTimeline：显示多 Agent 执行状态。
- ArtifactViewer：Markdown、代码、表格、图片、文件预览。
- ModelSwitcher：Provider + Model + Auto 调度模式。
- PermissionPrompt：本地文件/目录授权。

## 9. Electron 桌面端设计

用户已确认 Electron。

### 9.1 运行模式

建议第一版采用本地 API 进程模式：

```text
Electron Main
  -> 检测端口
  -> 启动 bundled QianYuan.Api
  -> 打开 Renderer
  -> Renderer 访问 http://127.0.0.1:{port}/api
```

后续支持云端 API：

- 本地模式：个人用户，数据本地或连接用户自己的数据库。
- 云端模式：企业 SaaS / 私有化部署，桌面端只作为客户端。

### 9.2 Electron 目录建议

```text
src/QianYuan.Desktop/
  ├─ package.json
  ├─ electron-builder.yml
  ├─ src/main/
  │   ├─ main.ts
  │   ├─ apiProcess.ts
  │   ├─ ipc.ts
  │   └─ updater.ts
  ├─ src/preload/
  │   └─ index.ts
  └─ scripts/
      ├─ package-api.js
      └─ after-sign.js
```

### 9.3 桌面能力

- macOS `.dmg` / Windows `.exe` 打包。
- 托盘、最小化、自动启动可后置。
- 自动更新可后置。
- 文件/目录选择通过 IPC，避免 Renderer 直接拥有过大权限。
- 本地 API 进程日志写入应用数据目录。
- 端口冲突自动切换。

## 10. Credits 计量规则

建议第一版采用简单、可解释的规则：

```text
基础文本模型：
  input  1 credit / 1,000 tokens
  output 3 credits / 1,000 tokens

高阶模型倍率：
  standard: 1x
  advanced: 2x
  premium: 5x

专家团任务：
  按每个 Agent 实际模型消耗累计
  Coordinator 调度不额外收费或低倍率收费

图像任务：
  按图片张数和尺寸固定扣费

深度研究：
  LLM 消耗 + 搜索/抓取步骤固定附加费
```

所有扣费都应先产生 estimate，再执行 consume：

1. 前端创建任务时调用估算接口。
2. 开始任务前检查余额。
3. 流式执行过程中记录 usage。
4. 任务完成后按实际用量结算，多退少补。
5. 失败任务按成功消耗的模型用量扣费，系统错误可自动返还。

## 11. 安全设计

- JWT Access Token 短有效期，Refresh Token 可撤销。
- 密码使用 ASP.NET Core PasswordHasher 或 Argon2id。
- API 默认要求授权，登录/注册/健康检查例外。
- 所有数据按 `UserId` 过滤。
- Electron 开启 `contextIsolation`，关闭 `nodeIntegration`。
- 文件系统操作必须显式授权目录。
- Tool / Skill 执行需要权限边界和审计日志。
- MCP / CLI 服务的敏感环境变量继续使用现有 AES 加密能力。
- Credits 交易使用数据库事务，避免并发扣成负数。

## 12. 分阶段实施计划

### Phase 0：设计与技术地基

- 完成本文档。
- 确认 PostgreSQL / MySQL 首选数据库。
- 确认是否需要保留 SQLite 单机模式。
- 明确 MVP 专家团模板。

### Phase 1：账户与数据库

- 扩展 `QianYuan.Data` 支持 PostgreSQL / MySQL。
- 新增 User / RefreshToken 实体和迁移。
- 新增 AuthController / AccountController。
- 前端新增 LoginPage / RegisterPage。
- `api.ts` 支持 Authorization header。

### Phase 2：Credits

- 新增 Wallet / Transaction / Plan / Subscription。
- 新增 Credits API。
- 接入 Chat / Image / WorkTask usage 记录。
- 前端新增余额、用量、交易流水。

### Phase 3：任务工作台

- 新增 WorkTask / WorkStep / Artifact。
- 将现有 Chat UI 改为三栏工作台。
- 新增任务列表、执行时间线、右侧结果区。
- 复用当前 SSE 流式状态机。

### Phase 4：专家团与多 Agent 编排

- 新增 ExpertTeamOrchestrator。
- 支持串行 workflow。
- 支持并行 fan-out / fan-in。
- 支持专家评审和最终汇总。
- UI 支持专家团模板选择和执行进度展示。

### Phase 5：Electron 桌面端

- 新增 `QianYuan.Desktop`。
- 打包 React dist 和 .NET API。
- macOS / Windows 打包脚本。
- 本地 API 进程管理。
- 文件授权 IPC。

### Phase 6：商业化与企业化

- 账单、发票、支付。
- 团队/组织/租户。
- 企业模型网关。
- 私有化部署脚本。
- IM / 小程序接入。

## 13. 第一批建议开发任务

建议下一步从 Phase 1 开始：

1. 改造 `QianYuan.Data` 数据库 Provider 配置，支持 PostgreSQL / MySQL。
2. 新增账户实体与 EF Core 映射。
3. 实现注册、登录、刷新 Token。
4. 前端加登录注册页和 Token 持久化。
5. 给现有 API 增加鉴权策略，但保留开发模式开关。

原因：账户和用户隔离是 Credits、任务、专家团、桌面多用户体验的地基。先做 UI 或专家团会很快遇到“数据属于谁、余额怎么扣、任务怎么隔离”的问题。
