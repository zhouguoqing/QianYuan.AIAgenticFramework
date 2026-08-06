# QianYuan 多轮对话第一阶段实现说明

## 一、阶段目标

本阶段围绕“会话持久化与完整消息存储”进行最小代价改造，目标是在不大范围重构现有项目的前提下，让对话会话、消息历史和基础会话管理能力从内存态升级为数据库持久化，并在前端侧边栏提供可浏览、搜索、切换、重命名和删除的会话入口。

## 二、快照与容错

- 改动前快照：`artifacts/multiturn-phase1-prechange-20260803-100810`
- 早期改动后快照：`artifacts/multiturn-phase1-postchange-20260803-102020`
- 最终验证后快照索引：`artifacts/multiturn-phase1-final-latest.txt`
- 本次验证临时目录：`artifacts/multiturn-phase1-smoke`

上述目录均保存在本地工作区，未进行云端上传。若后续需要回退，应优先使用改动前快照，避免影响其他历史阶段已有改动。

## 三、后端数据模型

### `src/QianYuan.Data/Entities/Conversations.cs`

新增三类会话持久化实体：

- `Conversation`：保存会话主记录，包括会话 ID、用户 ID、标题、绑定 Agent、状态、元数据、创建时间和更新时间。
- `ConversationMessage`：保存完整消息记录，包括角色、序列号、消息 JSON、软删除标记和创建时间。
- `ConversationTurn`：保存用户消息与助手消息之间的轮次关系，便于后续统计 token、审计多轮上下文和扩展追踪能力。

### `src/QianYuan.Data/QianYuanDbContext.cs`

扩展 EF Core 上下文：

- 新增 `Conversations`、`ConversationMessages`、`ConversationTurns` 三个 `DbSet`。
- 为会话、消息、轮次建立主键、外键和索引。
- 配置会话 ID、标题、状态、角色等字段长度。
- 配置会话到消息、会话到轮次的级联删除关系。

## 四、数据库初始化与持久化服务

### `src/QianYuan.Data/DataServiceCollectionExtensions.cs`

主要改动：

- 注册 `ISessionStore -> EfSessionStore`，避免运行时继续落回内存会话存储。
- 在数据库初始化流程中调用会话表初始化逻辑。
- 增加 SQLite、PostgreSQL、SQL Server 三类数据库的会话表建表脚本。
- 保持原有数据库初始化方式，不引入迁移体系，降低对现有项目的破坏面。

### `src/QianYuan.Data/Services/EfSessionStore.cs`

新增 EF 会话存储实现：

- `GetAsync`：从数据库恢复会话及其完整消息列表。
- `SaveAsync`：保存会话主信息、完整消息 JSON 和轮次关系。
- `DeleteAsync`：将会话标记为 `Deleted`，对前端和接口隐藏。
- `ListAsync`：按更新时间倒序返回会话摘要，并支持按用户过滤。

本次继续阶段额外修复：

- 修复 `ISessionStore` 未真正注册为 EF 实现的问题。
- 修复 `ConversationTurn` 通过导航集合新增时被 EF 误判为更新，导致 SQLite 下保存会话报并发异常的问题。
- 将接口默认会话标题从占位符改为中文 `新会话`。

## 五、流式对话与消息保存

### `src/QianYuan.Api/Controllers/ChatController.cs`

扩展 SSE 对话接口：

- 接收请求后优先从 `ISessionStore` 加载历史消息。
- 将当前用户消息追加到会话上下文。
- 通过 `StreamTranscriptBuilder` 汇总流式输出事件。
- 保存可见历史，包括助手文本、思考片段、工具调用、工具结果、警告和错误。
- 对话结束后写入数据库，确保刷新页面或重启后可恢复历史。

### `src/QianYuan.Api/Hubs/ChatHub.cs`

扩展 SignalR 对话入口：

- 注入 `ISessionStore`。
- 执行前加载历史会话消息。
- 执行后保存用户消息、助手文本和工具结果。

当前 SignalR 路径采用最小一致实现，已具备基础持久化能力；相比 SSE 路径，工具调用参数、思考片段、警告和错误的完整转录仍可在后续阶段继续抽象为公共转录服务。

### `src/QianYuan.Kernel/ReAct/ReActEngine.cs`

扩展 ReAct 流式事件输出：

- 对外透出工具调用参数增量。
- 对外透出工具调用结束事件。
- 为后端持久化工具调用轨迹和前端展示工具过程提供事件来源。

## 六、会话管理接口

### `src/QianYuan.Api/Controllers/CatalogControllers.cs`

扩展 `SessionsController`：

- `GET /api/sessions`：获取会话列表，支持 `q` 搜索、`ownerId` 过滤和 `take` 数量限制。
- `GET /api/sessions/{id}`：获取单个会话完整状态。
- `POST /api/sessions`：创建空会话。
- `PUT /api/sessions/{id}`：更新会话标题或绑定 Agent。
- `DELETE /api/sessions/{id}`：软删除会话。

新增请求模型：

- `SessionCreateRequest`
- `SessionUpdateRequest`

## 七、前端会话能力

### `src/QianYuan.Web/src/types/api.ts`

新增前后端对齐的数据类型：

- `ChatRoleDto`
- `ContentKindDto`
- `ContentPartDto`
- `ChatMessageDto`
- `SessionStateDto`
- `SessionCreateRequest`
- `SessionUpdateRequest`

### `src/QianYuan.Web/src/services/api.ts`

新增或扩展会话接口调用：

- `listSessions(q?)`
- `getSession(id)`
- `createSession(req)`
- `updateSession(id, req)`
- `deleteSession(id)`

### `src/QianYuan.Web/src/hooks/useChat.ts`

新增会话恢复和历史渲染逻辑：

- 监听 `sessionId` 变化并调用 `getSession` 恢复历史。
- 将后端 `ChatMessageDto` 映射为前端 `DisplayMessage`。
- 支持恢复用户消息、助手消息、思考消息、工具调用、工具结果、警告和错误。
- 保留原有发送消息和图片生成流程，避免扩大改动面。

### `src/QianYuan.Web/src/App.tsx`

新增当前会话记忆：

- 初始化时从 `localStorage` 读取最近会话 ID。
- 会话切换后写回 `localStorage`。
- 清空会话时同步移除本地记录。

### `src/QianYuan.Web/src/components/Sidebar.tsx`

扩展左侧会话列表：

- 支持会话搜索。
- 支持点击切换历史会话。
- 支持重命名会话。
- 支持删除会话。
- 保留原有列表刷新机制。

## 八、验证结果

已完成验证：

- `dotnet run --project artifacts/multiturn-phase1-smoke/SessionSmoke/SessionSmoke.csproj --no-restore`
  - 结果：通过。
  - 覆盖：数据库初始化、`ISessionStore` 注册、会话保存、完整消息恢复、工具结果恢复、会话列表、重命名、软删除。
- `dotnet build QianYuan.AgenticFramework.sln --no-restore`
  - 结果：通过。
- `npm run build --prefix src/QianYuan.Web`
  - 结果：通过。

已知警告：

- 构建过程中存在既有 `SQLitePCLRaw.lib.e_sqlite3` 高危漏洞警告，本阶段未修改依赖版本，未扩散处理。

## 九、当前边界与后续建议

- SSE 对话路径已保存更完整的可见流式历史。
- SignalR 对话路径已具备基础持久化，但工具调用参数、思考片段、警告和错误的持久化完整度低于 SSE。
- 后续建议将 `StreamTranscriptBuilder` 抽为公共服务，让 ChatController 与 ChatHub 复用同一套转录逻辑。
- 若后续需要生产级数据库演进，建议补正式 EF Core migration，而不是继续堆叠手写建表脚本。
