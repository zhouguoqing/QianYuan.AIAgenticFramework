# 多轮对话第三阶段实现说明（记忆系统与对话检索）

生成时间：2026-08-07

## 一、工作快照

开始前已保存当前状态快照，避免后续实现不可回退：

- 快照目录：`artifacts/snapshots/pre-phase3-multiturn-20260807-152450`
- 快照标记：`artifacts/snapshots/pre-phase3-multiturn-latest.txt`
- 快照内容：源码核心目录、文档目录、技能目录、`git status`、`git diff --binary`、未跟踪文件清单
- 特别处理：快照排除了 `artifacts/snapshots`、`.git`、`node_modules`、`bin`、`obj`、`dist`，避免再次出现快照自嵌套长路径问题

## 二、本次实现范围

本次参照《QianYuan 多轮对话功能规划》第三阶段，完成了一个最小可用但完整闭环的实现：

1. 工作空间记忆文件体系
2. 用户级长期记忆文件
3. 会话开始时自动注入本地记忆
4. `conversation_search` 历史会话检索工具
5. `memory_read` / `memory_write` 记忆读写工具
6. 会话 Markdown / JSON 导出 API
7. 前端会话列表导出入口

## 三、后端改动文件

### 1. `src/QianYuan.Core/Memory/IMemoryService.cs`

新增 Core 层记忆抽象：

- `MemoryContext`：描述当前会话的工作空间、用户、会话 ID
- `MemorySnapshot`：承载用户记忆、工作空间记忆、当日日志和对应本地文件路径
- `IMemoryService`：定义读取记忆、写入记忆、追加每日工作日志三个能力

该接口放在 Core 层，便于 API、Kernel、Builtin Skill 共同使用，避免内置技能直接依赖 API 项目。

### 2. `src/QianYuan.Api/Services/LocalMemoryService.cs`

新增本地文件记忆实现：

- 工作空间记忆目录：`<workspace>/.qianyuan/memory/`
- 工作空间长期记忆：`<workspace>/.qianyuan/memory/MEMORY.md`
- 工作空间每日日志：`<workspace>/.qianyuan/memory/YYYY-MM-DD.md`
- 用户级长期记忆：`~/.qianyuan/MEMORY.md`
- 用户级单次写入限制：最多 4000 字符
- 文件并发写入保护：基于路径级 `SemaphoreSlim`，避免多个会话同时写入同一记忆文件时互相覆盖

当请求没有传入有效 `workspacePath` 时，会退回 API 内容根目录作为工作空间。

### 3. `src/QianYuan.Skills.Builtin/Memory/ConversationMemorySkill.cs`

新增内置记忆技能，技能 ID 为：`qianyuan.memory`。

暴露三个工具：

- `conversation_search`
  - 参数：`query`、`start_date`、`end_date`、`limit`
  - 能力：基于已持久化会话列表和消息内容做关键词搜索，返回相关会话片段
- `memory_read`
  - 参数：`scope`
  - 可选范围：`workspace`、`user`、`daily`、`all`
  - 能力：读取本地 QIANYUAN 记忆文件
- `memory_write`
  - 参数：`scope`、`content`
  - 可选范围：`workspace`、`user`、`daily`
  - 能力：追加写入本地长期记忆或当日日志

技能内部通过 `IServiceScopeFactory` 在调用时解析 `ISessionStore`，避免单例技能直接持有 Scoped 数据库服务。

### 4. `src/QianYuan.Skills.Builtin/BuiltinSkillsExtensions.cs`

新增注册扩展：

- `AddConversationMemorySkill()`

该方法将 `ConversationMemorySkill` 注册为内置 `ISkill`，由原有 `RegisterSkillsFromServices()` 统一挂载到技能管理器。

### 5. `src/QianYuan.Api/Program.cs`

新增注册：

- `IMemoryService -> LocalMemoryService`
- `AddConversationMemorySkill()`
- 默认 Agent 预加载技能中加入 `qianyuan.memory`

同时修复了一处默认提示词末尾乱码导致的 C# 字符串编译问题，恢复为：

- “工具会根据用户意图渐进式加载——只暴露当前可能用到的技能。”

### 6. `src/QianYuan.Api/Controllers/ChatController.cs`

新增记忆注入和自动日志能力：

- 在每次 `chat/stream` 开始前读取：
  - 用户级长期记忆
  - 工作空间长期记忆
- 将记忆内容合并到系统提示词中，帮助新会话具备跨会话上下文
- 默认预加载 `qianyuan.memory`，确保模型可以主动调用记忆和检索工具
- 回复完成后自动追加每日工作日志，记录：
  - 会话标题
  - 会话 ID
  - 用户输入摘要
  - 助手回复摘要

同时修复一处原有乱码错误提示，恢复为：

- “云端大模型服务 '{providerOverride}' 未注册或未启用。”

### 7. `src/QianYuan.Api/Controllers/CatalogControllers.cs`

在 `SessionsController` 中新增：

- `GET /api/sessions/{id}/export?format=markdown`
- `GET /api/sessions/{id}/export?format=json`

导出能力：

- Markdown：按用户、助手、工具、系统角色组织会话内容
- JSON：导出完整 `SessionState`
- 文件名：基于会话标题生成，自动清理非法文件名字符

同时修复新建会话默认标题乱码，恢复为：

- `新会话`

## 四、前端改动文件

### 1. `src/QianYuan.Web/src/services/api.ts`

新增：

- `exportSession(id, format)`

能力：

- 请求 `/api/sessions/{id}/export`
- 支持 `markdown` 和 `json`
- 解析 `Content-Disposition` 文件名
- 返回 `Blob` 和文件名，供前端下载

### 2. `src/QianYuan.Web/src/components/Sidebar.tsx`

会话列表每条记录新增两个操作：

- `导出`：下载 Markdown
- `JSON`：下载 JSON

保留原有：

- 点击会话加载历史
- 重命名
- 删除
- 搜索会话

## 五、当前已达到的第三阶段验收点

已完成：

- 新会话/新请求会自动读取并注入工作空间和用户级长期记忆
- 会话完成后自动追加每日工作日志
- `conversation_search` 工具可以搜索历史会话并返回相关片段
- `memory_read` / `memory_write` 可以读写本地记忆文件
- 支持会话 Markdown / JSON 导出
- 前端提供会话导出入口

部分实现但未做复杂扩展：

- 30 天以上日志“蒸馏到 MEMORY.md”暂未接入 LLM 摘要，只完成了每日日志与长期记忆文件的基础结构
- `conversation_search` 当前采用关键词匹配 + 时间排序，不是向量检索
- 记忆写入由模型工具调用或会话结束日志触发，尚未增加更复杂的用户偏好识别策略

## 六、验证结果

已执行：

```powershell
dotnet build QianYuan.AgenticFramework.sln
npm run build --prefix src/QianYuan.Web
```

结果：

- 后端构建通过
- 前端构建通过
- 本次触及文件未发现需替换的历史品牌词

构建中仍存在项目既有 warning，例如依赖安全提示、nullable warning、chunk size warning；本次未扩大范围处理。

## 七、补充更新：会话记录交互与清空能力

第三阶段完成后，根据前端反馈继续补充了两个会话记录相关功能。

### 1. 会话记录操作区优化

实现效果：

- 默认只显示会话标题和摘要。
- 鼠标悬停会话行时显示 `…`、`删除`、`重命名`。
- 点击 `…` 展开 `导出` 和 `JSON`。
- 删除和重命名保持为独立按钮，避免与导出入口混在一起。

涉及文件：

- `src/QianYuan.Web/src/components/Sidebar.tsx`
- `src/QianYuan.Web/src/styles.css`

### 2. 清空会话记录

实现效果：

- 会话记录搜索栏上方新增 `清空会话记录`。
- 点击后确认清空全部会话记录。
- 清空时同步清理当前聊天态和当前会话 ID。
- 清空接口改为 `POST /api/sessions/clear`，避免 `DELETE /api/sessions` 在部分环境返回 405。
- 后端保留 `DELETE /api/sessions` 兼容旧调用。
- 存储层新增 `ClearAsync`，并防止已删除会话被旧流式保存重新激活。

涉及文件：

- `src/QianYuan.Api/Controllers/CatalogControllers.cs`
- `src/QianYuan.Core/Memory/ISessionStore.cs`
- `src/QianYuan.Data/Services/EfSessionStore.cs`
- `src/QianYuan.Kernel/QianYuanKernelExtensions.cs`
- `src/QianYuan.Web/src/App.tsx`
- `src/QianYuan.Web/src/components/Sidebar.tsx`
- `src/QianYuan.Web/src/services/api.ts`

验证：

```powershell
dotnet build
npm run build --prefix src/QianYuan.Web
```

结果：后端和前端构建均通过。运行中的旧后端需要重启后才能使用新的 `POST /api/sessions/clear`。