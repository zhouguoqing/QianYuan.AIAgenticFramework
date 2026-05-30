# API 接口说明

该文档基于项目中实际控制器实现编写，列出可用的 HTTP 与 SSE/JSON-RPC 路由、请求体和返回字段。

基础信息
- 基础 URL（开发示例）：`http://localhost:5000`
- 认证：多数端点期望在 `Authorization: Bearer <API_KEY>` 头中传入 API Key（视部署与中间件而定）

主要端点（按控制器）

**Chat (SSE 流式)**
- 路径：`POST /api/chat/stream`
- 描述：基于 SSE （text/event-stream）按事件推送模型生成的流式片段。每个 SSE 事件的 payload 为 JSON。
- 请求体（ChatStreamRequest）：

```
{
  "agentId": "optional-agent-id",
  "sessionId": "optional-session-id",
  "ownerId": "optional-owner-id",
  "userText": "用户要发送的文本",
  "images": [{ "url": "data:..." | "https://...", "base64": "...", "mime": "image/png" }],
  "provider": "可选 provider id，如 qwen",
  "model": "可选模型覆盖",
  "skills": ["skill.id"],
  "maxIterations": 3
}
```

- 事件类型（event 名称）示例：`session`, `text`, `thinking`, `tool_call_start`, `tool_call_args`, `tool_call_end`, `tool_observation`, `usage`, `start`, `end`, `warning`, `error`, `done`。
- chunk payload 字段（在 event data 内）示例：

```
{
  "kind": "TextDelta", // 枚举名
  "text": "部分文本",
  "toolCallId": null,
  "toolName": null,
  "toolArgsJson": null,
  "finishReason": null,
  "model": "qwen-...",
  "agentId": "proactive-agent",
  "skillId": null,
  "step": 1,
  "usage": { "input": 10, "output": 20, "cacheRead":0, "cacheWrite":0 }
}
```

**Images（图片生成/编辑）**
- 路径：`POST /api/images/generate`
- 描述：基于 OpenAI 兼容 provider 发起图像生成或 image-to-image edits
- 请求体（ImageGenerationRequest）：

```
{
  "mode": "text-to-image" | "image-to-image",
  "prompt": "生成提示文本",
  "images": [ { "base64": "...", "url": "data:...", "mime": "image/png" } ],
  "provider": "optional-provider-id",
  "model": "optional-model",
  "size": "1024x1024"
}
```

- 成功返回（200）示例（ImageGenerationResponse）：

```
{
  "provider": "openai",
  "model": "gpt-image-1",
  "url": "https://.../image.png",    // 或者 base64 字段
  "base64": null,
  "mime": "image/png",
  "revisedPrompt": "可选的服务端修改后的提示"
}
```

- 常见错误返回：`400`（无 prompt / 模式错误）、`404`（未配置 provider）、`503`（provider 未配置 API key）、`502`（上游 provider 错误或无图片数据）

**MCP server（JSON-RPC & SSE）**
- 路径：`POST /api/mcp` — JSON-RPC 2.0 over POST。请求体为 JSON-RPC 请求对象，返回 JSON-RPC 响应或空对象。
- 路径：`GET /api/mcp/events` — SSE 长连接用于接收通知，服务器每 25s 发起 ping。

**Catalog / 管理类**

Agents
- `GET /api/agents` — 返回 agent 列表，每项示例：

```
{ "id": "proactive-agent", "name": "Proactive Agent", "description": "...", "tags": ["example"] }
```

Skills
- `GET /api/skills` — 返回技能清单，项内含 `Id, Name, Description, Tags, ApproximateToolCount, RequiresNetwork, RequiresFilesystem, Enabled`。
- `GET /api/skills/relevant?q=...&topK=8` — 基于查询返回相关技能（数组）。
- `GET /api/skills/{skillId}/tools` — 返回：

```
{
  "skillId":"...",
  "systemPromptFragment":"...",
  "enabled":true,
  "tools": [ { "name":"tool","description":"...","jsonSchema":{...},"skillId":"..." } ]
}
```
- `POST /api/skills/{skillId}/enabled` — 请求体 `{ "enabled": true }`，返回 `{ skillId, enabled }` 或 `404`。
- `POST /api/skills/register/mcp-stdio` — 注册一个 MCP-stdio 后端作为 skill；请求体示例：

```
{ "serverId":"myserver", "command":"/path/to/bin","arguments":["--flag"], "environment":{}}
```

Providers
- `GET /api/providers` — 返回 providers 与可用模型：

```
{
  "defaultProviderId": "openai",
  "providers": [ { "providerId":"openai","DefaultModel":"gpt-4o","Models":["gpt-4o","gpt-4o-mini"], "capabilities":["chat","images"] } ]
}
```

Sessions
- `GET /api/sessions?ownerId=...&take=50` — 列表最近会话。
- `GET /api/sessions/{id}` — 返回 `SessionState`（会话 id、agentId、messages、title 等），`404` 表示未找到。
- `DELETE /api/sessions/{id}` — 删除会话，返回 `204 No Content`。

**DingTalk 集成**
- `POST /api/dingtalk/webhook` — 针对钉钉的 outgoing webhook；如果请求头包含 `timestamp` 与 `sign`，会进行签名校验；处理为异步 fire-and-forget，默认返回 `200` 与 `{ ok: true }`。可能返回 `401`（签名不匹配）或 `400`（无效 JSON）。

错误处理与状态码
- `400`：请求体无效或缺少必填字段（示例：images/generate 缺少 prompt）。
- `401`：认证或外部平台签名失败（例如 DingTalk）。
- `404`：请求的 skill/provider/agent 未配置。
- `409`：资源冲突（例如重复注册）。
- `502/503`：上游 provider 错误或服务不可用。

示例 curl

文本流（SSE）示例：

```
curl -N -X POST http://localhost:5000/api/chat/stream \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"userText":"请生成会议要点","provider":"qwen"}'
```

图片生成示例：

```
curl -s -X POST http://localhost:5000/api/images/generate \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"prompt":"A futuristic city at sunrise","mode":"text-to-image","size":"1024x1024"}'
```

MCP JSON-RPC 示例（POST）

```
curl -s -X POST http://localhost:5000/api/mcp \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":"1","method":"mcp.ping","params":{}}'
```

附注
- 文档基于当前代码实现生成；如果你希望我把请求/响应示例进一步格式化为 OpenAPI/Swagger 或添加示例请求-响应对到 `docs/` 下的独立文件，我可以继续生成。
