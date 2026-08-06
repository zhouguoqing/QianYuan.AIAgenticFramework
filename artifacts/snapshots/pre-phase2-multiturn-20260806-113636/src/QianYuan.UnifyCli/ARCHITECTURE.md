# UnifyCli 架构设计文档

## 系统架构

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Agent / ReAct Engine                         │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                    ┌────────▼────────┐
                    │  Skill Manager  │
                    │  Skill Registry │
                    └────────┬────────┘
                             │
           ┌─────────────────┼─────────────────┐
           │                 │                 │
    ┌──────▼──────┐ ┌────────▼────────┐ ┌─────▼──────────┐
    │VisionSkill  │ │CliServiceSkill  │ │WebSearchSkill  │
    │(Built-in)   │ │(UnifyCli)       │ │(Built-in)      │
    └──────┬──────┘ └────────┬────────┘ └─────┬──────────┘
           │                 │                 │
           │        ┌────────▼────────┐        │
           │        │CLI Service ▲    │        │
           │        │Registry    │    │        │
           │        └────────┬────────┘        │
           │                 │                 │
           │        ┌────────▼────────┐        │
           │        │CliService #1    │        │
           │        │CliService #2    │        │
           │        │CliService #N    │        │
           │        └────────┬────────┘        │
           │                 │                 │
           │     ┌───────────┼───────────┐     │
           │     │           │           │     │
    ┌──────▼─────▼─┐ ┌────────▼──────┐ ┌─────▼───────┐
    │ LLM Provider │ │HTTP Client    │ │ Other API   │
    │              │ │(with Auth)    │ │   Clients   │
    └──────┬──────┘ └────────┬──────┘ └─────┬───────┘
           │                 │              │
    ┌──────▼──────────────────┼──────────────▼──────┐
    │         External Services                      │
    │  ┌──────────────┐  ┌──────────────┐           │
    │  │ GitHub API   │  │ Slack API    │ ...       │
    │  └──────────────┘  └──────────────┘           │
    └────────────────────────────────────────────────┘
```

## 组件关系

### 1. Agent 层
- **ReAct Agent**: 调用工具执行任务
- **Skill Manager**: 管理和发现可用的 Skill
- **Tool Dispatcher**: 将工具调用路由到对应的 Skill

### 2. Skill 层
- **CliServiceSkill**: UnifyCli 的 Skill 适配器
- **内置 Skill**: Vision, WebSearch 等
- **第三方 Skill**: MCP 客户端等

### 3. UnifyCli 层 (新增)
- **CLI Service Registry**: 管理所有已注册的 CLI 服务
- **CLI Service**: 一个完整的服务，包含多个方法
- **CLI Method**: 一个单一的、映射到 HTTP 端点的方法
- **HTTP Client**: 执行实际的 HTTP 请求
- **Authentication**: 处理不同的认证方式

### 4. 外部服务层
- **REST API**: GitHub、Slack、天气服务等
- **HTTP/HTTPS 端点**: 任何网络可访问的服务

## 数据流

### 调用流程

```
1. Agent 决定调用某个工具
   ↓
2. ReAct 获取所有可用的 CliServiceSkill 工具
   ↓
3. Agent 选择 "get_user" 工具，传递参数 {"userId": "123"}
   ↓
4. CliServiceSkill.InvokeAsync("get_user", "{\"userId\": \"123\"}")
   ↓
5. 找到对应的 CLI Service 和 CLI Method
   ↓
6. UnifyHttpClient 构建 HTTP 请求
   - 插值路径参数: /users/123
   - 应用认证
   - 设置超时和重试
   ↓
7. 执行 HTTP 请求到外部服务
   ↓
8. 接收响应，进行响应转换（如果配置了）
   ↓
9. 返回 CliInvocationResult
   ↓
10. CliServiceSkill 转换为 SkillInvocationResult
    ↓
11. Agent 接收结果，作为 LLM 的观察
```

### 参数流

```
LLM Output (JSON):
{
  "tool_name": "get_user",
  "arguments": {"userId": "123"}
}
    ↓
CliServiceSkill.InvokeAsync("get_user", "{\"userId\": \"123\"}")
    ↓
UnifyHttpClient.ExecuteAsync(method, parametersJson)
    ↓
参数解析和插值:
- 提取 userId = "123"
- PathTemplate: "/users/{userId}" → "/users/123"
- QueryParams 中的 $userId 替换
- RequestBody 中的 $userId 替换
    ↓
构建 HTTP 请求
    ↓
执行请求，获取响应
    ↓
CliInvocationResult {
  JsonContent: "{ \"id\": \"123\", \"name\": \"John\" }",
  HumanSummary: "Retrieved user John",
  IsError: false,
  StatusCode: 200,
  ExecutionTimeMs: 150
}
    ↓
SkillInvocationResult {
  JsonContent: "{ \"id\": \"123\", \"name\": \"John\" }",
  HumanSummary: "Retrieved user John",
  IsError: false
}
    ↓
LLM 接收为观察，继续推理
```

## 认证架构

```
┌──────────────────────────────┐
│  IAuthenticationProvider     │
│  (Abstract Interface)        │
└──────────────┬───────────────┘
               │
    ┌──────────┼──────────┬──────────┬──────────────┐
    │          │          │          │              │
┌───▼──┐ ┌────▼───┐ ┌────▼────┐ ┌──▼──────┐ ┌─────▼─────┐
│Basic │ │Bearer  │ │ API Key │ │Custom   │ │   None    │
│Auth  │ │ Token  │ │         │ │Headers  │ │           │
└───┬──┘ └────┬───┘ └────┬────┘ └──┬──────┘ └─────┬─────┘
    │         │          │          │             │
    └─────────┼──────────┼──────────┼─────────────┘
              │          │          │
              ▼          ▼          ▼
         HttpRequestMessage with Authentication Headers/Params
```

## 扩展性设计

### 1. 添加新的 CLI 服务

```csharp
// 定义服务
var newService = new CliServiceDefinition
{
    Id = "my.service",
    Name = "My Service",
    BaseUri = "https://api.myservice.com"
};

// 定义方法
var method = new CliMethodDefinition { /* ... */ };
newService.RegisterMethod(method);

// 注册到容器
services.AddCliService(newService);
```

### 2. 添加自定义认证

```csharp
public class CustomAuthProvider : IAuthenticationProvider
{
    public string Description => "Custom Authentication";
    
    public Task ApplyAsync(HttpRequestMessage request)
    {
        // 实现自定义认证逻辑
        return Task.CompletedTask;
    }
}
```

### 3. 添加响应转换

```csharp
public class CustomResponseTransformer : IResponseTransformer
{
    public Task<string> TransformAsync(
        int statusCode,
        string content,
        IReadOnlyDictionary<string, string> headers)
    {
        // 实现自定义转换逻辑
        return Task.FromResult(transformedContent);
    }
}
```

## 集成模式

### 模式 1：简单整合

```
CLI 服务 → Skill → Agent
```

适用于：简单的单个 API 集成

### 模式 2：多服务聚合

```
┌─ Weather API ─┐
├─ GitHub API ──┼─ Skill Registry ─ Skill Manager ─ Agent
├─ Slack API ───┤
└─ Custom API ──┘
```

适用于：多个相关的 API 需要在同一个 Agent 中使用

### 模式 3：代理模式

```
Agent ─ Proxy Skill ─┬─ CLI Weather ─ External API
                    ├─ CLI GitHub ── External API
                    └─ CLI Slack ─── External API
```

适用于：需要对多个 CLI 服务进行统一控制/监控

## 性能考虑

### 1. 缓存
- CLI 方法定义被缓存到内存
- Tool 定义在第一次调用时缓存
- 减少重复的解析和验证

### 2. 连接复用
- HttpClient 使用单一实例
- TCP 连接池自动管理
- 避免连接泄漏

### 3. 超时管理
- 默认 30 秒超时
- 支持按方法配置
- 防止悬挂请求

### 4. 重试策略
- 默认 1 次重试
- 指数级延迟（可配置）
- 仅对幂等操作重试

## 安全考虑

### 1. 认证安全
- ✓ 支持多种安全的认证方式
- ✓ 密钥不存储在配置文件中
- ✓ 支持环境变量和密钥管理服务

### 2. 传输安全
- ✓ 强制 HTTPS
- ✓ TLS 版本验证
- ✓ 证书验证

### 3. 请求安全
- ✓ URL 编码参数
- ✓ 超时防止 DOS
- ✓ 重试限制

### 4. 响应安全
- ✓ JSON 验证
- ✓ 大小限制（可配置）
- ✓ 安全的错误处理

## 监控和日志

### 1. 执行指标
```csharp
CliInvocationResult {
    ExecutionTimeMs,  // 执行耗时
    StatusCode,       // HTTP 状态码
    IsError          // 是否出错
}
```

### 2. 结构化日志
- Service 初始化事件
- 方法调用事件
- 认证事件
- 错误事件

### 3. 可观测性
- 支持分布式追踪
- 支持自定义日志记录
- 支持性能监控集成

## 版本控制

### 后向兼容性
- 新增认证类型时，旧代码继续工作
- 新增 CLI 方法时，现有代码不受影响
- CliInvocationResult 扩展时，现有代码继续工作

### 迁移路径
- 从直接 HTTP 调用迁移到 CLI 服务
- 从 CLI 服务迁移到更高级的抽象
- 支持混合模式（既有 CLI 又有直接 HTTP）
