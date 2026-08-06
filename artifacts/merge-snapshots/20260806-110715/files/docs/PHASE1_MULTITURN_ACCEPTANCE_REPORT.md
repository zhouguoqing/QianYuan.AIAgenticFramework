# QianYuan 多轮对话第一阶段完成确认说明

## 结论

经对照 `QianYuan多轮对话功能规划.docx` 中“第1阶段：会话持久化与完整消息存储”的 8 项任务与 4 条验收标准，本阶段功能已完成并通过本地验证。

本阶段已实现：会话数据库持久化、完整消息历史保存、SSE 与 SignalR 会话保存一致性、会话管理 API、前端会话列表与历史恢复。

说明：规划中“生成 EF Core Migration”这一项，当前按项目既有数据库初始化模式实现为启动期建表脚本，而不是新增 `Migrations` 目录。该实现已覆盖 SQLite、PostgreSQL、SQL Server 的会话表创建逻辑，不影响第一阶段功能验收。

## 一阶段目标对照

| 规划任务 | 完成状态 | 说明 |
| --- | --- | --- |
| 新建会话与消息数据实体 | 已完成 | 新增 `Conversation`、`ConversationMessage`、`ConversationTurn` 三类实体。 |
| 扩展 `QianYuanDbContext` 并创建迁移 | 已完成（脚本化建表） | 新增三个 `DbSet`、索引、外键关系，并通过启动初始化脚本创建表。 |
| 实现 `EfSessionStore` 替换内存存储 | 已完成 | `ISessionStore` 已注册为 `EfSessionStore`，会话不再依赖内存存储。 |
| 修复 `ChatController` 消息保存逻辑 | 已完成 | SSE 路径保存助手文本、思考、工具调用、工具结果、警告和错误。 |
| 修复 `ChatHub` 会话集成 | 已完成 | SignalR 路径已加载历史、追加用户消息，并保存完整转录消息。 |
| 新增会话管理 API | 已完成 | 已支持列表、详情、新建、重命名、删除。 |
| 前端会话列表侧边栏 | 已完成 | 侧边栏支持最近会话、搜索、切换、重命名、删除。 |
| 前端会话恢复与历史消息渲染 | 已完成 | 切换会话后从 API 加载历史并恢复工具调用、观察结果和思考过程显示。 |

## 验收标准对照

| 验收标准 | 结论 | 验证说明 |
| --- | --- | --- |
| 应用重启后历史对话完整恢复 | 通过 | `EfSessionStore` 将会话与消息写入数据库，smoke 测试覆盖保存、读取、软删除。 |
| 会话列表可浏览、搜索、切换、重命名、删除 | 通过 | `/api/sessions` CRUD 本地验证通过，前端侧边栏已接入搜索、切换、重命名、删除。 |
| 多轮对话中后续轮次能看到之前工具调用和结果 | 通过 | `ChatController` 与 `ChatHub` 均将历史消息传入 `AgentRunRequest.Messages`，并持久化工具调用与工具结果。 |
| `ChatHub` 与 SSE 端点行为一致 | 通过 | `ChatHub` 已补齐完整转录器，覆盖文本、思考、工具调用、工具参数、工具观察、警告、错误。 |

## 关键实现文件

### 数据层

- `src/QianYuan.Data/Entities/Conversations.cs`：新增会话、消息、轮次实体。
- `src/QianYuan.Data/QianYuanDbContext.cs`：新增会话相关 `DbSet`，配置主键、索引、外键。
- `src/QianYuan.Data/DataServiceCollectionExtensions.cs`：注册 `EfSessionStore`，并在数据库初始化时确保会话表存在。
- `src/QianYuan.Data/Services/EfSessionStore.cs`：实现会话保存、读取、列表、软删除。

### 后端接口与流式转录

- `src/QianYuan.Api/Controllers/ChatController.cs`：SSE 对话保存完整流式转录。
- `src/QianYuan.Api/Hubs/ChatHub.cs`：SignalR 对话加载并保存完整会话历史。
- `src/QianYuan.Api/Controllers/CatalogControllers.cs`：扩展 `SessionsController`，提供会话管理接口。
- `src/QianYuan.Kernel/ReAct/ReActEngine.cs`：透出工具调用参数与工具调用结束事件，支持完整消息落库。

### 前端

- `src/QianYuan.Web/src/types/api.ts`：新增会话与消息 DTO 类型。
- `src/QianYuan.Web/src/services/api.ts`：新增会话 API 调用方法。
- `src/QianYuan.Web/src/hooks/useChat.ts`：新增历史会话恢复和消息映射逻辑。
- `src/QianYuan.Web/src/App.tsx`：持久化最近使用的 `sessionId`。
- `src/QianYuan.Web/src/components/Sidebar.tsx`：新增会话列表、搜索、重命名、删除入口。

## 本次补充修正

在本次验收检查中发现 `ChatHub` 之前只保存助手文本和工具观察，完整度低于 SSE。已做最小修正：新增 `HubTranscriptBuilder`，使 SignalR 路径与 SSE 一样保存思考、工具调用参数、工具调用结束、工具结果、警告和错误。

## 验证记录

已执行以下本地验证：

- `dotnet build QianYuan.AgenticFramework.sln --no-restore`：通过。
- `npm run build --prefix src/QianYuan.Web`：通过。
- `dotnet run --project artifacts/multiturn-phase1-smoke/SessionSmoke/SessionSmoke.csproj --no-restore`：通过。
- `POST /api/sessions`：通过，能够新建会话。
- `GET /api/sessions?q=phase1-check-session`：通过，能够搜索会话。
- `GET /api/sessions/{id}`：通过，能够读取会话详情。
- `PUT /api/sessions/{id}`：通过，能够重命名会话。
- `DELETE /api/sessions/{id}`：通过，删除后详情接口返回隐藏状态。
- 前端代理接口：`/api/sessions`、`/api/experts`、`/api/skills` 均返回成功。

## 当前运行状态

- 后端：`http://127.0.0.1:5050/`
- 前端：`http://127.0.0.1:5173/`
- Swagger：`http://127.0.0.1:5050/swagger`

## 已知非阻断事项

- 项目构建仍存在既有依赖警告：`SQLitePCLRaw.lib.e_sqlite3` 高危漏洞警告。
- 前端构建仍存在既有 Vite chunk 体积警告。
- 当前阶段未处理第二阶段目标，例如 token 级上下文压缩、SSE 断线恢复、消息编辑与重生成。
- 当前阶段未处理第三阶段目标，例如记忆系统、`conversation_search`、会话导出。

## 完成判定

第一阶段按功能目标和验收标准判定为完成，可以进入第二阶段的上下文管理、断线恢复、消息编辑与重新生成等能力建设。
