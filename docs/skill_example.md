# 使用 `api-gateway` Skill 的真实示例

说明：下面示例针对仓库内的 `api-gateway` skill（Maton-managed API routing），演示 agent 在预加载 `api-gateway` 后如何：
1) 执行只读查询以探测目标连接与资源；
2) 在获得结果后向用户汇报并请求确认；
3) 在用户确认后执行（或模拟执行）具体的读取/写入操作。

示例场景（更真实）：查询公司内部项目服务（通过 Maton 代理）获得项目统计并生成邮件摘要。

请求示例（SSE 流式）

```
curl -N -X POST http://localhost:5000/api/chat/stream \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "agentId": "proactive-agent",
    "userText": "目标：查询内部项目列表并生成一段 120 字的邮件摘要，包含完成率和待办优先级建议。请首先用 read-only 调用确认连接与资源标识，然后在得到结果后向我汇报并等待确认执行任何修改操作。",
    "provider": "qwen",
    "skills": ["api-gateway"],
    "maxIterations": 4
  }'
```

示例中 agent 可能发出的第一个工具调用（只读，Maton API 路由）：

tool_call_args 示例（GET connections）：

```
{
  "toolName": "maton.api",
  "toolArgsJson": {
    "method": "GET",
    "url": "https://api.maton.ai/connections?app=zoho-projects&status=ACTIVE",
    "headers": { "Authorization": "Bearer $MATON_API_KEY" }
  }
}
```

如果返回连接信息，agent 会选择合适的 `connection_id` 并进行资源查询，例如：

tool_call_args 示例（查询项目列表）：

```
{
  "toolName": "maton.api",
  "toolArgsJson": {
    "method": "GET",
    "url": "https://api.maton.ai/zoho-projects/api/projects?status=active&limit=100",
    "headers": { "Authorization": "Bearer $MATON_API_KEY", "Maton-Connection": "{connection_id}" }
  }
}
```

预期 `tool_observation`（工具返回的观测）示例：

```
{
  "status": 200,
  "body": {
    "projects": [ { "id": "p1", "name": "Alpha", "completion": 0.92 }, { "id": "p2", "name": "Beta", "completion": 0.54 } ],
    "total": 12
  }
}
```

接着 agent 会在 SSE 中逐步输出 `text` 片段，汇总观察并生成建议，例如：

```
{ "summary": "当前共有 12 个活跃项目，平均完成率 78%。建议优先处理 Beta（进度 54%）的阻塞项并在下周例会上汇报 Alpha 的风险点。" }
```

如果任务需要修改（例如批量更新项目状态），agent 应先展示将要执行的非 GET 请求并等待用户确认：

示例（需要用户确认的变更请求说明，由 agent 提示）：

```
我打算对 2 个项目发起 PATCH 请求以更新状态为 "at-risk"：
- PATCH /projects/p2 { "status": "at-risk" }
请回复 "yes" 以确认或 "no" 取消。
```

调试与验证
- 若要查看 `api-gateway` skill 的工具与 schema：调用 `GET /api/skills/api-gateway/tools`。
- 若需本地测试 Maton 调用，可先用 CLI：

```
maton api '/zoho-projects/api/projects?status=active&limit=10' --connection {connection_id}
```

下一步我可以：
- 将上面 SSE 示例自动化为一个 Node.js 客户端脚本并加入 `docs/`，或
- 把示例中的 `tool_call_args` 转为 `tool_call_start` / `tool_call_args` 的实际 SSE 片段示例并加入 `docs/`。

---

## 逐行 SSE 事件序列示例

下面是一个按顺序的 SSE（Server-Sent Events）示例，展示 agent 运行过程中 `tool_call_start`、`tool_call_args`、`tool_observation` 与 `text` 事件如何逐行发送。每一行为 SSE 的原始文本（生产环境中每个 `data:` 行后跟一个空行）。

示例（逐行）：

event: session
data: {"sessionId":"s12345","agentId":"proactive-agent"}

event: start
data: {"timestamp":"2026-05-30T08:00:00Z"}

event: thinking
data: {"kind":"ThinkingDelta","text":"检测 Maton 连接中..."}

event: tool_call_start
data: {"kind":"ToolCallStart","toolCallId":"tc1","toolName":"maton.api"}

event: tool_call_args
data: {"kind":"ToolCallArgsDelta","toolCallId":"tc1","toolName":"maton.api","toolArgsJson":{"method":"GET","url":"https://api.maton.ai/connections?app=zoho-projects&status=ACTIVE","headers":{"Authorization":"Bearer $MATON_API_KEY"}}}

event: tool_observation
data: {"kind":"ToolObservation","toolCallId":"tc1","toolName":"maton.api","status":200,"body":{"connections":[{"connection_id":"conn_abc","status":"ACTIVE"}]}}

event: text
data: {"kind":"TextDelta","text":"已发现活跃连接 conn_abc，将使用该连接查询项目列表。"}

event: tool_call_start
data: {"kind":"ToolCallStart","toolCallId":"tc2","toolName":"maton.api"}

event: tool_call_args
data: {"kind":"ToolCallArgsDelta","toolCallId":"tc2","toolName":"maton.api","toolArgsJson":{"method":"GET","url":"https://api.maton.ai/zoho-projects/api/projects?status=active&limit=100","headers":{"Authorization":"Bearer $MATON_API_KEY","Maton-Connection":"conn_abc"}}}

event: tool_observation
data: {"kind":"ToolObservation","toolCallId":"tc2","toolName":"maton.api","status":200,"body":{"projects":[{"id":"p1","name":"Alpha","completion":0.92},{"id":"p2","name":"Beta","completion":0.54}],"total":12}}

event: text
data: {"kind":"TextDelta","text":"查询到 12 个活跃项目，平均完成率约 78%。建议优先处理 Beta（54%）的阻塞项。"}

event: done
data: {"sessionId":"s12345"}

说明：
- 每个 SSE 事件由 `event:` 行和 `data:` 行组成，事件之间以空行分隔。
- 客户端可按顺序解析事件：在收到 `tool_call_args` 后，后端或 Skill 负责执行相应工具（例如通过 MCP 或 HTTP 调用），并将结果作为 `tool_observation` 发送回来。
- `toolCallId` 可用于将同一调用的 args 与 observation 关联。

