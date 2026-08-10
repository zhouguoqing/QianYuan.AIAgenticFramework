# 聊天修复报告：对话闪回首页 + 角色设定丢失

- 日期：2026-08-10
- 状态：✅ 已修复并验证
- 涉及范围：`src/QianYuan.Web/src/hooks/useChat.ts`、`src/QianYuan.Api/Controllers/ChatController.cs`
- 说明：本文档记录两项问题的定位、根因、修复与验证。

---

## 一、问题 1：对话结束后闪退回首页（闪退）

### 1.1 现象

从首页发起对话后，界面会莫名其妙地"闪回"欢迎页（HomeLanding），而不是保持在聊天界面。但会话其实**已经保存成功**（侧边栏会话列表能看到新会话及消息条数）。复现时浏览器控制台出现一次 `GET /api/sessions/{id}` 返回 **404** 的请求。

### 1.2 根因链

```
前端发送消息
  └─> streamChat 流式开始，收到 "session" 事件 → onSession(newId) → setSessionId(newId)
       └─> useChat 的"切换会话时重载历史" effect 因 sessionId 变化被触发
            └─> 调用 getSession(newId)
                 └─> 后端 ChatController 只在流式【结束后】才 SaveAsync 落库
                      └─> 此时会话尚未保存 → GET /api/sessions/{id} 返回 404
                           └─> effect 的 .catch(() => setMessages([])) 清空内存消息
                                └─> hasActiveChat = false → 界面闪回首页
```

关键点：**前端在流式对话过程中，因 sessionId 变化触发了会话重载，而重载的会话此刻在后端还不存在（404）**，`setMessages([])` 把内存里正在流式累积的消息清空了。

### 1.3 修复（双保险）

#### 前端：`src/QianYuan.Web/src/hooks/useChat.ts`

1. 新增 `streamingRef` 标记流式对话是否进行中。
2. 会话重载 effect 增加守卫：`if (streamingRef.current) return` —— 流式对话期间消息已在内存中（`applyChunk` 实时累积），不需要重载。
3. `.catch` 不再清空 `messages`（防御性，避免加载失败时把整个界面清空回首页）。

```ts
// 流式进行中标记
const streamingRef = useRef(false)

// 会话重载 effect
useEffect(() => {
  const seq = ++loadSeqRef.current
  if (!opts.sessionId) { setMessages([]); return }
  if (streamingRef.current) return            // ← 新增：流式期间跳过重载
  getSession(opts.sessionId)
    .then(session => { /* setMessages(...) */ })
    .catch(() => { /* 不再清空 messages */ })  // ← 修改
  ...
}, [opts.sessionId])

// send 的聊天分支
setBusy(true)
streamingRef.current = true                    // ← 新增
...
} finally {
  setBusy(false)
  streamingRef.current = false                 // ← 新增
  ...
}
```

#### 后端：`src/QianYuan.Api/Controllers/ChatController.cs`

在流式开始前（`WriteSse("session")` 之前）先把含用户消息的会话落库，使 `GET /api/sessions/{id}` 立即返回 200 而非 404：

```csharp
state.AgentId = agent.Id;

// 流式开始前先落库，避免前端 getSession 拿到 404
try
{
    await _sessions.SaveAsync(state, ct).ConfigureAwait(false);
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to persist session {SessionId} before streaming", sessionId);
}
```

### 1.4 验证

- 浏览器实测：首页输入消息 → 发送 → 界面**保持在聊天视图**，用户消息 + AI 回复完整渲染。
- 网络请求：全部 200，无 `/api/sessions/{id}` 404。
- 后端直测：发起新对话后 `GET /api/sessions/{id}` 立即返回 200（修复前 404）。

---

## 二、问题 2：默认 Agent 角色设定丢失（模型自称 Codex）

### 2.1 现象

向默认 Agent（qianyuan.default）提问"你是谁"，模型回复"我是 Codex / 我是 Codex，一个务实的 AI 编程助手"，而不是 QianYuan（乾元）。该问题**时灵时不灵**（有时正常自称 QianYuan，有时变 Codex）。

### 2.2 根因链

```
LocalMemoryService.ReadAsync
  └─> EnsureFileAsync 自动创建记忆文件（不存在时）：
        ~/.qianyuan/MEMORY.md              → "# QIANYUAN 用户长期记忆"
        <api根>/.qianyuan/memory/MEMORY.md → "# QIANYUAN 工作空间长期记忆"
  └─> 记忆文件只有标题，无实质内容

ChatController.BuildMemoryPrompt
  └─> 用 !string.IsNullOrWhiteSpace 判断 → 标题也算"非空" → memoryPrompt 非空

ChatController.BuildSystemPromptOverride
  └─> [storeAgent?.SystemPrompt, expertPrompt, memoryPrompt]
      └─> storeAgent / expertPrompt 为空 → 只剩 memoryPrompt
           └─> SystemPromptOverride = 纯记忆片段（无角色设定）

ReActAgent.RunAsync
  └─> SystemPromptOverride 非空 → 覆盖默认角色 _def.SystemPrompt（"你是 QianYuan…"）
       └─> 模型看不到角色设定 → 按自身身份自称 Codex
```

**时灵时不灵的原因**：第一次对话前记忆文件尚不存在 → `memoryPrompt` 为 null → 角色生效（自称 QianYuan）；`ReadAsync` 在第一次对话时创建了记忆文件 → 之后的对话 `memoryPrompt` 非空 → 角色丢失（自称 Codex）。

### 2.3 修复：`src/QianYuan.Api/Controllers/ChatController.cs`

`BuildMemoryPrompt` 增加 `TrimMeaningful` 过滤：丢弃 Markdown 标题行（如自动初始化的 `# QIANYUAN 用户长期记忆`）和空白行，只有存在**实质内容**的记忆才会注入系统提示，纯标题的自动初始化文件不再覆盖角色设定。

```csharp
private static string? BuildMemoryPrompt(MemorySnapshot snapshot)
{
    var sections = new List<string>();
    var userMemory = TrimMeaningful(snapshot.UserMemory);
    var workspaceMemory = TrimMeaningful(snapshot.WorkspaceMemory);
    if (userMemory is not null) sections.Add($"用户级长期记忆：\n{userMemory}");
    if (workspaceMemory is not null) sections.Add($"工作空间长期记忆：\n{workspaceMemory}");

    return sections.Count == 0
        ? null
        : "以下是 QIANYUAN 本地记忆，仅用于保持跨会话上下文。…\n\n" + string.Join("\n\n", sections);
}

private static string? TrimMeaningful(string? text)
{
    if (string.IsNullOrWhiteSpace(text)) return null;
    var lines = text
        .Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
        .ToArray();
    return lines.Length == 0 ? null : string.Join("\n", lines);
}
```

### 2.4 验证

- 默认对话（无 systemPrompt）问"你是谁" → 回复"我是乾元智能助手，帮你处理代码、检索信息和完成各类任务。"（✅ 不再自称 Codex）
- 带自定义 systemPrompt（"你是测试角色小明"）→ 模型正确遵循"我是小明"（说明 system 传递链路本身正常，问题确在默认角色的覆盖逻辑）

---

## 三、涉及文件汇总

| 文件 | 改动 | 目的 |
|------|------|------|
| `src/QianYuan.Web/src/hooks/useChat.ts` | 新增 `streamingRef`，会话重载 effect 跳过流式期间的 getSession；`.catch` 不再清空消息 | 修复闪回首页 |
| `src/QianYuan.Api/Controllers/ChatController.cs` | 流式开始前 `SaveAsync` 落库；`BuildMemoryPrompt` 增加 `TrimMeaningful` 过滤 | 修复 getSession 404 + 修复角色设定丢失 |

## 四、排查方法（可复用）

1. **闪回首页**：用浏览器抓网络请求，定位流式请求后紧跟的非 200 请求（`GET /api/sessions/{id}` 404 即为信号）。
2. **角色丢失**：做两个对照实验——
   - 带自定义 systemPrompt 问"你是谁"（验证 system 传递链路是否正常）；
   - 不带 systemPrompt 用默认 Agent 问"你是谁"（验证默认角色是否生效）。
   若前者正常、后者异常，问题在默认角色的 SystemPromptOverride 组装逻辑，而非上游模型或编码。

## 五、遗留建议

- **记忆语义**：当前记忆（`memoryPrompt`）走的是 `SystemPromptOverride`（覆盖语义）。更合理的做法是让记忆作为"附加上下文"与角色设定共存，而非覆盖。后续可考虑让 `IAgent` 暴露 `SystemPrompt`，使 override 始终包含角色基础，避免"有实质记忆就丢角色"的潜在问题。
- **Codex 上游**：部分 `gpt-5.5` 上游在无角色设定时会主动自称 Codex，属模型自身行为；只要角色设定稳定注入即可规避。
