# QianYuan 多轮对话第三阶段详细说明文档

生成时间：2026-08-07

本文档用于说明本次“多轮对话第三阶段：记忆系统与对话检索”的实现内容、涉及文件、功能边界、验证结果与后续建议。

---

## 1. 本次工作目标

根据《QianYuan 多轮对话功能规划.docx》第三阶段要求，本阶段目标是为 QianYuan 多轮对话系统补齐跨会话上下文能力，主要包括：

1. 工作空间记忆系统
2. 用户级记忆系统
3. 记忆注入机制
4. 历史会话检索工具 `conversation_search`
5. 记忆读写工具 `memory_read` / `memory_write`
6. 会话导出功能
7. 前端会话导出入口

本次实现遵循“最小代价、低破坏、可回退”的原则：

- 不重构原有多轮会话主链路
- 不变更现有数据库结构
- 不引入向量数据库等重依赖
- 优先复用已存在的 `ISessionStore`、`SessionState`、技能注册与 ReAct 工具调用机制
- 保持本地化记忆文件，不上传云端

---

## 2. 开始前快照与工作留痕

### 2.1 快照位置

开始实现前已保存当前状态快照：

- 快照目录：`artifacts/snapshots/pre-phase3-multiturn-20260807-152450`
- 快照标记：`artifacts/snapshots/pre-phase3-multiturn-latest.txt`

### 2.2 快照内容

快照包含：

- `src` 核心源码目录
- `docs` 文档目录
- `skills/custom` 自定义技能目录
- `git-status-short.txt`
- `git-diff.patch`
- `git-untracked-files.txt`

### 2.3 快照策略说明

本次快照刻意排除了以下目录：

- `.git`
- `node_modules`
- `bin`
- `obj`
- `dist`
- `artifacts/snapshots`

原因：此前出现过快照目录自嵌套导致 Windows 长路径删除困难的问题。本次快照使用“核心源码 + 差异补丁 + 未跟踪文件清单”的方式，既保证可追溯，也避免快照递归膨胀。

### 2.4 工作留痕文件

- 规划提取：`artifacts/multiturn-phase3-plan-extracted.txt`
- 工作留痕：`artifacts/multiturn-phase3-worklog.txt`
- Git 状态记录：`artifacts/multiturn-phase3-git-status.txt`
- Diff 统计：`artifacts/multiturn-phase3-diff-stat.txt`

---

## 3. 总体架构设计

本次实现分为四层：

1. Core 抽象层
   - 定义记忆服务接口和数据结构
2. API 服务层
   - 实现本地文件记忆读写
   - 在聊天链路中注入记忆
   - 在会话完成后追加工作日志
   - 提供会话导出 API
3. Builtin Skill 层
   - 新增记忆与历史会话检索技能
   - 通过 ReAct 工具机制让模型主动调用
4. Web 前端层
   - 在会话列表中增加 Markdown / JSON 导出按钮

整体数据流如下：

```text
用户发送消息
  ↓
ChatController.Stream
  ↓
读取用户级记忆 + 工作空间记忆
  ↓
拼接进 SystemPromptOverride
  ↓
ReActAgent / ReActEngine 执行
  ↓
模型可调用 qianyuan.memory 技能
  ↓
conversation_search / memory_read / memory_write
  ↓
回复完成后自动追加每日工作日志
  ↓
保存会话到 ISessionStore
```

---

## 4. 详细改动文件说明

## 4.1 Core 层

### 文件：`src/QianYuan.Core/Memory/IMemoryService.cs`

新增内容：

- `MemoryContext`
- `MemorySnapshot`
- `IMemoryService`

### 作用

该文件定义了记忆系统的统一抽象，避免 API、Kernel、Builtin Skill 之间发生不合理的依赖反转。

### 关键类型

#### `MemoryContext`

用于描述当前会话上下文：

- `WorkspacePath`：当前工作空间路径
- `WorkspaceLabel`：工作空间展示名
- `OwnerId`：用户 ID
- `SessionId`：会话 ID

#### `MemorySnapshot`

用于承载读取到的记忆内容：

- `UserMemory`：用户级长期记忆
- `WorkspaceMemory`：工作空间长期记忆
- `TodayLog`：当日工作日志
- `UserMemoryPath`：用户记忆文件路径
- `WorkspaceMemoryPath`：工作空间记忆文件路径
- `DailyLogPath`：当日日志文件路径

#### `IMemoryService`

定义三个核心能力：

- `ReadAsync`：读取本地记忆
- `WriteMemoryAsync`：写入长期记忆或当日日志
- `AppendDailyLogAsync`：会话完成后自动追加每日工作日志

---

## 4.2 API 服务层

### 文件：`src/QianYuan.Api/Services/LocalMemoryService.cs`

新增本地文件记忆实现。

### 记忆文件结构

#### 工作空间级记忆

```text
<workspace>/.qianyuan/memory/MEMORY.md
```

用途：保存当前项目/工作空间内长期有效的信息，例如：

- 项目约定
- 技术栈偏好
- 业务背景
- 长期任务目标
- 已确认的重要结论

#### 工作空间每日工作日志

```text
<workspace>/.qianyuan/memory/YYYY-MM-DD.md
```

用途：追加记录当天完成的实质性会话摘要，例如：

- 会话标题
- 会话 ID
- 用户输入摘要
- 助手回复摘要

#### 用户级长期记忆

```text
~/.qianyuan/MEMORY.md
```

用途：保存跨项目共享的用户偏好，例如：

- 用户表达习惯
- 长期偏好
- 常用工作方式
- 稳定身份信息

### 写入限制

- 用户级单次写入限制：最多 4000 字符
- 每日日志中用户输入和助手回复分别截断到 2000 字符
- 读取时最多读取后 16000 字符，避免系统提示词过大

### 并发保护

`LocalMemoryService` 使用路径级 `SemaphoreSlim` 对文件追加写入加锁，降低多个会话同时写入同一文件时发生内容交叉或覆盖的风险。

### 工作空间路径回退

如果请求中没有提供有效 `workspacePath`，系统会回退到 API 内容根目录作为工作空间目录。

---

## 4.3 Builtin Skill 层

### 文件：`src/QianYuan.Skills.Builtin/Memory/ConversationMemorySkill.cs`

新增内置技能：

```text
qianyuan.memory
```

### 技能名称

```text
QIANYUAN Memory and Conversation Search
```

### 技能用途

该技能让模型具备以下能力：

1. 搜索历史会话
2. 读取本地记忆
3. 写入本地记忆

### 暴露工具 1：`conversation_search`

#### 参数

```json
{
  "query": "关键词",
  "start_date": "2026-08-01",
  "end_date": "2026-08-07",
  "limit": 5
}

```

#### 功能

基于已持久化的 `ISessionStore` 搜索历史会话。

当前实现方式：

- 读取最近最多 200 个会话摘要
- 根据 `ownerId` 做用户范围过滤
- 根据 `start_date` / `end_date` 做时间过滤
- 加载会话详情
- 对消息文本、工具参数、工具结果做关键词匹配
- 返回最多 `limit` 条会话
- 每条会话最多返回 3 条相关片段

#### 返回内容

返回 JSON，包含：

- `sessionId`
- `title`
- `agentId`
- `updatedAt`
- `messageCount`
- `snippets`

### 暴露工具 2：`memory_read`

#### 参数

```json
{
  "scope": "all"
}
```

可选范围：

- `workspace`
- `user`
- `daily`
- `all`

#### 功能

读取本地记忆文件内容，返回给模型用于上下文判断。

### 暴露工具 3：`memory_write`

#### 参数

```json
{
  "scope": "workspace",
  "content": "需要长期保存的项目约定或用户偏好"
}
```

可选范围：

- `workspace`
- `user`
- `daily`

#### 功能

将模型认为值得长期保存的信息追加到本地记忆文件中。

### 依赖解析方式

由于内置技能是单例注册，不能直接持有 Scoped 数据库上下文。本次实现中，技能在调用时通过：

```csharp
IServiceScopeFactory.CreateScope()
```

动态解析 `ISessionStore`，避免生命周期冲突。

---

## 4.4 技能注册

### 文件：`src/QianYuan.Skills.Builtin/BuiltinSkillsExtensions.cs`

新增扩展方法：

```csharp
AddConversationMemorySkill()
```

作用：

- 将 `ConversationMemorySkill` 注册为内置 `ISkill`
- 通过既有 `RegisterSkillsFromServices()` 自动挂载到技能管理器

---

## 4.5 应用启动注册

### 文件：`src/QianYuan.Api/Program.cs`

新增注册：

```csharp
builder.Services.AddSingleton<IMemoryService, LocalMemoryService>();
builder.Services.AddConversationMemorySkill();
```

默认 Agent 的预加载技能中新增：

```text
qianyuan.memory
```

### 说明

这样默认 Agent 在常规对话中就能看到记忆工具，并可根据需要主动调用 `conversation_search`、`memory_read` 或 `memory_write`。

---

## 4.6 聊天链路改造

### 文件：`src/QianYuan.Api/Controllers/ChatController.cs`

### 改动 1：注入 `IMemoryService`

`ChatController` 新增依赖：

```csharp
IMemoryService _memory
```

### 改动 2：会话开始时读取本地记忆

在 `Stream` 方法中构造：

```csharp
MemoryContext
```

并调用：

```csharp
_memory.ReadAsync(...)
```

读取：

- 用户级长期记忆
- 工作空间长期记忆

### 改动 3：记忆注入系统提示词

新增：

```csharp
BuildMemoryPrompt(...)
```

将本地记忆合并到 `SystemPromptOverride` 中。

注入内容会提示模型：

- 这是 QIANYUAN 本地记忆
- 仅用于跨会话上下文保持
- 不要主动泄露记忆文件路径

### 改动 4：会话结束后追加每日工作日志

在模型回复完成并保存会话后，调用：

```csharp
AppendDailyLogAsync(...)
```

记录：

- 会话标题
- 会话 ID
- 用户输入摘要
- 助手回复摘要

### 改动 5：记忆技能预加载策略

如果请求中传入了显式技能或专家绑定技能，系统会自动补入：

```text
qianyuan.memory
```

默认 Agent 场景则依赖 `Program.cs` 中的默认预加载技能，避免覆盖原本默认技能列表。

---

## 4.7 会话导出 API

### 文件：`src/QianYuan.Api/Controllers/CatalogControllers.cs`

### 新增接口

```http
GET /api/sessions/{id}/export?format=markdown
GET /api/sessions/{id}/export?format=json
```

### Markdown 导出

Markdown 导出会包含：

- 会话标题
- 会话 ID
- Agent ID
- 创建时间
- 更新时间
- 用户消息
- 助手消息
- 工具调用
- 工具结果
- 图片引用

### JSON 导出

JSON 导出完整 `SessionState`，用于调试、迁移或备份。

### 文件名处理

导出文件名基于会话标题生成，并自动替换非法文件名字符。

### 同步修复

修复新建会话默认标题乱码：

```text
新会话
```

---

## 4.8 前端 API 改动

### 文件：`src/QianYuan.Web/src/services/api.ts`

新增：

```ts
exportSession(id, format)
```

功能：

- 调用 `/api/sessions/{id}/export`
- 支持 `markdown` 和 `json`
- 读取响应 `Blob`
- 解析 `Content-Disposition` 中的文件名
- 如果响应头没有文件名，则使用兜底文件名

---

## 4.9 前端界面改动

### 文件：`src/QianYuan.Web/src/components/Sidebar.tsx`

会话列表每条记录新增两个按钮：

- `导出`：导出 Markdown
- `JSON`：导出 JSON

保留原有功能：

- 点击会话加载历史
- 重命名
- 删除
- 搜索会话
- 会话列表自动刷新

---

## 5. 实现后的功能行为

## 5.1 新会话记忆注入

当用户开始新的聊天请求时，系统会自动读取：

```text
~/.qianyuan/MEMORY.md
<workspace>/.qianyuan/memory/MEMORY.md
```

并注入到系统提示词。

这样模型在新会话中也能获得跨会话长期上下文。

## 5.2 每日工作日志自动追加

每次模型回复完成后，系统会追加类似内容到：

```text
<workspace>/.qianyuan/memory/YYYY-MM-DD.md
```

示例结构：

```markdown
## 15:30:12 新会话
- 会话：xxxxxxxxxxxxxxxx
- 用户：请帮我分析第三阶段实现方案
- 回复：本次实现包含记忆系统、会话检索、导出能力...
```

## 5.3 模型主动搜索历史会话

模型可以调用：

```text
conversation_search
```

用于回答类似问题：

- “之前我们讨论过 image2 的问题吗？”
- “找一下上次多轮对话第二阶段修了什么”
- “搜索我之前关于技能页面的反馈”

## 5.4 模型读写长期记忆

模型可以调用：

```text
memory_read
memory_write
```

用于读取或写入稳定上下文。

建议写入内容应是：

- 用户明确表达的长期偏好
- 项目稳定约定
- 后续会反复使用的信息
- 已完成的重要工作结论

不建议写入：

- 临时闲聊
- 一次性问题
- 敏感凭证
- 未确认事实

## 5.5 会话导出

前端侧边栏会话列表中：

- 点击 `导出` 下载 Markdown
- 点击 `JSON` 下载 JSON

---

## 6. 验证情况

### 6.1 后端构建

已执行：

```powershell
dotnet build QianYuan.AgenticFramework.sln
```

结果：通过。

### 6.2 前端构建

已执行：

```powershell
npm run build --prefix src/QianYuan.Web
```

结果：通过。

### 6.3 品牌词检查

已检查本次触及文件，未发现需替换的历史品牌词

### 6.4 已知 warning

构建过程中仍存在项目既有 warning，例如：

- NuGet 包安全提示
- nullable warning
- 前端 chunk size warning

这些 warning 不是本次第三阶段实现引入的核心错误，本次未扩大范围处理。

---

## 7. 本次未完全覆盖的规划项

第三阶段规划中有一些高级目标，本次做了基础闭环，但未做重型实现。

### 7.1 30 天以上日志蒸馏

规划要求：

```text
30 天以上日志蒸馏到 MEMORY.md
```

本次完成：

- 每日日志文件结构
- 长期记忆文件结构
- 记忆读写能力

尚未完成：

- 自动扫描 30 天以上日志
- 调用 LLM 摘要蒸馏
- 自动合并到长期 `MEMORY.md`

### 7.2 语义检索 / 向量检索

本次 `conversation_search` 使用：

- 关键词匹配
- 时间过滤
- 最近会话排序

尚未接入：

- embedding
- 向量数据库
- 语义相似度召回

原因：避免引入重依赖，先确保功能可用。

### 7.3 复杂记忆治理

本次 `memory_write` 允许模型写入本地文件，但尚未实现：

- 记忆去重
- 记忆冲突检测
- 用户确认式写入
- 敏感信息拦截
- 记忆编辑 UI

后续建议增加记忆治理层，避免长期记忆污染。

---

## 8. 回退方式

如果本次第三阶段改动需要回退，可参考快照：

```text
artifacts/snapshots/pre-phase3-multiturn-20260807-152450
```

该快照包含：

- 改动前源码副本
- 改动前 Git 状态
- 改动前 diff patch
- 未跟踪文件清单

也可以根据 Git diff 手动回退以下本次新增/修改文件。

---

## 9. 本次新增文件清单

```text
src/QianYuan.Core/Memory/IMemoryService.cs
src/QianYuan.Api/Services/LocalMemoryService.cs
src/QianYuan.Skills.Builtin/Memory/ConversationMemorySkill.cs
docs/PHASE3_MULTITURN_IMPLEMENTATION.md
docs/PHASE3_MULTITURN_DETAILED_REPORT.md
artifacts/multiturn-phase3-plan-extracted.txt
artifacts/multiturn-phase3-worklog.txt
artifacts/multiturn-phase3-git-status.txt
artifacts/multiturn-phase3-diff-stat.txt
```

---

## 10. 本次修改文件清单

```text
src/QianYuan.Api/Controllers/CatalogControllers.cs
src/QianYuan.Api/Controllers/ChatController.cs
src/QianYuan.Api/Program.cs
src/QianYuan.Skills.Builtin/BuiltinSkillsExtensions.cs
src/QianYuan.Web/src/components/Sidebar.tsx
src/QianYuan.Web/src/services/api.ts
```

说明：`scripts/start.ps1` 在本轮开始前已经是修改状态，本次没有主动修改该文件。

---

## 11. 后续建议

建议下一步按以下顺序推进：

1. 启动项目并在真实前端验证会话导出按钮
2. 进行一次真实对话，确认 `.qianyuan/memory/YYYY-MM-DD.md` 自动追加
3. 手动写入一条 `MEMORY.md`，确认新会话可被模型引用
4. 用自然语言测试 `conversation_search` 是否能找到历史会话
5. 增加记忆管理 UI，允许用户查看、编辑、删除长期记忆
6. 增加记忆写入确认机制，避免模型误写入
7. 后续再接入向量检索和 30 天日志蒸馏

---

## 12. 当前结论

本次第三阶段已完成“可用闭环”：

- 有本地记忆文件
- 有会话开始记忆注入
- 有会话结束日志追加
- 有历史会话检索工具
- 有记忆读写工具
- 有会话导出 API
- 有前端导出入口

该实现不依赖外部云存储，符合本地化和低破坏原则，可作为后续高级记忆治理和语义检索的基础版本。
