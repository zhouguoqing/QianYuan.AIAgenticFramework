# API 使用示例

示例 1：使用 `curl` 调用 `execute`

```
API_KEY=your_key_here
curl -s -X POST http://localhost:5000/api/agents/execute \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "agentId":"proactive-agent",
    "provider":"qwen",
    "input":"请基于以下要点写一段 100 字的周报：完成 A，开始 B，计划 C",
    "options": { "stream": false }
  }'
```

预期返回（示例）：

```
{
  "id":"run_001",
  "status":"completed",
  "output":"本周完成了 A，启动了 B，计划下周继续 C。..."
}
```

示例 2：通过 WebSocket 接收流式输出（node.js 简要示例）

```
const WebSocket = require('ws');
const ws = new WebSocket('ws://localhost:5000/api/stream', {
  headers: { Authorization: 'Bearer ' + process.env.API_KEY }
});

ws.on('open', () => {
  ws.send(JSON.stringify({ type: 'execute', agentId: 'proactive-agent', input: '请生成摘要', provider: 'qwen' }));
});

ws.on('message', (data) => {
  try { const evt = JSON.parse(data.toString()); console.log(evt); } catch (e) { console.log(data.toString()); }
});
```

说明：根据你本地运行的服务地址与认证方式调整 `localhost` 与 `Authorization` 头。
