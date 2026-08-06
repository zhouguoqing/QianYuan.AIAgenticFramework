# QianYuan.AgenticFramework 项目长期记忆

## 项目概况
- .NET 10 解决方案 + React 19 / Vite WebUI，自研 Agentic 框架
- 解决方案: `QianYuan.AgenticFramework.sln`；一键启停: `scripts/start.sh`
- API: http://localhost:5050 (Swagger /swagger)；WebUI: http://localhost:5173
- 上游 LLM 默认 openai provider → `http://20.246.64.40/v1`（new-api 网关），模型 GPT-5.5
- Provider 配置: `src/QianYuan.Api/appsettings.json` 的 `QianYuan:OpenAICompatProviders`

## 关键约定 / 踩坑
- **HttpClient 代理坑**: 本机有 `http_proxy=http://127.0.0.1:49761`（WorkBuddy 沙箱代理）。.NET HttpClient 在 Unix/macOS 默认会读取此环境变量，导致 LLM 请求被代理转发后请求体 chunked 编码被破坏（上游报 `invalid byte in chunk length` HTTP 400）。OpenAICompat provider 已通过 `SocketsHttpHandler { UseProxy = false }` 修复。**其他 provider（Anthropic/Gemini/QwenNative/AzureOpenAI）若遇到类似问题，同样需要 UseProxy=false。**
- 启动 API 时若端口 5050 被占（遗留进程），先 `kill $(lsof -ti :5050)` 再启动。
- appsettings 中 `SkillDirectories` 用相对路径（`./skills` 等），`dotnet run` 工作目录需为仓库根才能正确加载 Markdown 技能。
