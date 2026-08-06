# UnifyCli - 统一的 HTTPS 服务 CLI 封装

## 概述

`QianYuan.UnifyCli` 是一个通用库，用于将现有的 HTTPS 服务和第三方 API 统一封装为 CLI 方法，支持在 Skill 中调用。

### 核心特性

- **统一的参数和返回值** - 所有 HTTP 服务调用都通过统一的 JSON 参数和返回值接口
- **多种认证支持** - Basic Auth, Bearer Token, API Key, OAuth2, 自定义 Header
- **自动重试和超时** - 内置网络故障处理和超时管理
- **响应转换** - 支持 JSON 响应的自动转换和提取
- **Skill 集成** - 将 CLI 服务直接暴露为 Skill 工具
- **渐进式加载** - 支持延迟初始化，减少内存占用

## 架构

### 核心模块

```
Abstractions/
├── IAuthenticationProvider.cs    # 认证提供程序接口
├── ICliMethod.cs                 # CLI 方法定义
├── ICliService.cs                # CLI 服务接口
└── ICliServiceRegistry.cs        # 服务注册表

Implementation/
├── AuthenticationProviders.cs    # 认证实现（Basic, Bearer, API Key 等）
├── UnifyHttpClient.cs            # HTTP 客户端，处理请求/响应
├── CliMethodDefinition.cs        # CLI 方法定义实现
├── CliServiceDefinition.cs       # CLI 服务实现
└── CliServiceRegistry.cs         # 服务注册表实现

Skills/
└── CliServiceSkill.cs            # Skill 适配器，将 CLI 服务暴露为工具
```

## 使用示例

### 1. 基本使用

```csharp
using QianYuan.UnifyCli.Implementation;
using QianYuan.UnifyCli.Abstractions;
using System.Text.Json;

// 创建一个 CLI 服务
var service = new CliServiceDefinition
{
    Id = "weather.api",
    Name = "Weather API",
    Description = "Get weather information",
    BaseUri = "https://api.weather.com"
};

// 定义一个 CLI 方法
var getCurrentWeather = new CliMethodDefinition
{
    Id = "get_current_weather",
    Name = "Get Current Weather",
    Description = "Get current weather for a location",
    BaseUri = "https://api.weather.com",
    HttpMethod = "GET",
    PathTemplate = "/v1/current",
    ParametersSchema = JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new
        {
            latitude = new { type = "number" },
            longitude = new { type = "number" }
        },
        required = new[] { "latitude", "longitude" }
    }),
    QueryParams = new Dictionary<string, string>
    {
        { "lat", "$latitude" },
        { "lon", "$longitude" }
    }
};

// 注册方法到服务
service.RegisterMethod(getCurrentWeather);

// 调用方法
var result = await service.InvokeAsync(
    "get_current_weather",
    "{\"latitude\": 39.9, \"longitude\": 116.4}"
);

if (!result.IsError)
{
    Console.WriteLine($"Result: {result.JsonContent}");
    Console.WriteLine($"Summary: {result.HumanSummary}");
}
```

### 2. 添加认证

```csharp
// 创建 Bearer Token 认证
var authOptions = new AuthenticationOptions
{
    Type = "bearer",
    Token = "your-api-token"
};

var authFactory = new AuthenticationProviderFactory();
var auth = authFactory.Create(authOptions);

var service = new CliServiceDefinition
{
    Id = "github.api",
    Name = "GitHub API",
    BaseUri = "https://api.github.com",
    DefaultAuthenticationProvider = auth
};
```

### 3. 支持的认证类型

#### Basic Authentication
```csharp
var options = new AuthenticationOptions
{
    Type = "basic",
    Username = "user@example.com",
    Password = "password123"
};
```

#### Bearer Token
```csharp
var options = new AuthenticationOptions
{
    Type = "bearer",
    Token = "eyJhbGciOiJIUzI1NiIs..."
};
```

#### API Key (Header)
```csharp
var options = new AuthenticationOptions
{
    Type = "api_key",
    Token = "sk-123456789",
    HeaderName = "X-API-Key"
};
```

#### API Key (Query Parameter)
```csharp
var options = new AuthenticationOptions
{
    Type = "api_key",
    Token = "sk-123456789",
    QueryParamName = "api_key"
};
```

#### Custom Headers
```csharp
var options = new AuthenticationOptions
{
    Type = "custom_header",
    CustomHeaders = new Dictionary<string, string>
    {
        { "Authorization", "Bearer token" },
        { "X-Custom-Header", "value" }
    }
};
```

### 4. Dependency Injection 集成

```csharp
var services = new ServiceCollection();

// 添加 UnifyCli
services.AddUnifyCli();

// 注册一个 CLI 服务
var weatherService = WeatherServiceExample.CreateWeatherService();
services.AddCliService(weatherService);

// 自动将 CLI 服务暴露为 Skill
services.AddCliServiceSkill("weather.api");

var sp = services.BuildServiceProvider();

// 获取技能管理器
var skillManager = sp.GetRequiredService<ISkillManager>();
var skill = await skillManager.GetAsync("qianyuan.cli.weather.api");
```

### 5. 复杂的请求/响应处理

```csharp
// 带有请求体的 POST 方法
var createUserMethod = new CliMethodDefinition
{
    Id = "create_user",
    Name = "Create User",
    BaseUri = "https://api.example.com",
    HttpMethod = "POST",
    PathTemplate = "/v1/users",
    ParametersSchema = JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new
        {
            name = new { type = "string" },
            email = new { type = "string" }
        }
    }),
    RequestBodyTemplate = "$.",  // 发送整个参数作为 JSON 体
    RequestHeaders = new Dictionary<string, string>
    {
        { "Content-Type", "application/json" }
    }
};
```

### 6. 响应转换

```csharp
// 使用 JSON 路径提取响应数据
var transformer = new JsonPathResponseTransformer("$.data.user");

var method = new CliMethodDefinition
{
    // ... 其他配置
    ResponseTransformer = transformer
};
```

## 参数和返回值格式

### 输入参数 (JSON Schema)

所有 CLI 方法接受 JSON Schema 定义的参数：

```json
{
  "type": "object",
  "properties": {
    "city": { "type": "string", "description": "City name" },
    "units": { "type": "string", "description": "Temperature units" }
  },
  "required": ["city"]
}
```

参数值通过 `$paramName` 语法插入到：
- **URL 路径** - `PathTemplate: "/weather/{city}"` + `$city` 
- **查询参数** - `QueryParams: { "units": "$units" }`
- **请求体** - `RequestBodyTemplate: "$."`（发送所有参数）

### 返回值格式

所有调用返回统一的 `CliInvocationResult`：

```csharp
public sealed class CliInvocationResult
{
    public string JsonContent { get; init; }      // 机器可读的 JSON 内容
    public string? HumanSummary { get; init; }    // 人类可读的摘要
    public bool IsError { get; init; }            // 是否出错
    public int? StatusCode { get; init; }         // HTTP 状态码
    public long ExecutionTimeMs { get; init; }    // 执行时间（毫秒）
}
```

## 配置选项

### CliMethodDefinition 配置

```csharp
public class CliMethodDefinition : ICliMethod
{
    public int TimeoutMs { get; init; } = 30000;        // 请求超时时间
    public int RetryCount { get; init; } = 1;           // 重试次数
    public int RetryDelayMs { get; init; } = 100;       // 重试延迟
    public IReadOnlyList<string> Tags { get; init; }    // 用于分类和发现的标签
}
```

## 示例服务

库中包含三个现成的示例服务实现：

1. **WeatherServiceExample** - OpenWeatherMap API 集成
2. **GitHubServiceExample** - GitHub REST API 集成
3. **SlackServiceExample** - Slack API 集成

使用示例：

```csharp
var weatherService = WeatherServiceExample.CreateWeatherService();
var githubService = GitHubServiceExample.CreateGitHubService(gitHubToken: "your-token");
var slackService = SlackServiceExample.CreateSlackService(slackToken: "your-token");
```

## 与 Skill 系统集成

UnifyCli 与 QianYuan.Kernel 的 Skill 系统无缝集成：

```csharp
// CliServiceSkill 自动转换 CLI 方法为工具
public class CliServiceSkill : ISkill
{
    // 每个 CLI 方法都成为一个工具
    public async ValueTask<IReadOnlyList<ToolDefinition>> GetToolsAsync()
    {
        // 返回基于 CLI 方法的工具定义
    }
    
    // 工具调用被委托给 CLI 服务
    public async ValueTask<SkillInvocationResult> InvokeAsync(
        string toolName, 
        string argumentsJson,
        SkillInvocationContext context)
    {
        // 将工具名称映射到 CLI 方法并执行
    }
}
```

## 最佳实践

1. **参数验证** - 在 ParametersSchema 中清晰定义参数约束
2. **错误处理** - 使用 `CliInvocationResult.Error()` 创建错误响应
3. **超时设置** - 根据服务特性合理设置 TimeoutMs
4. **重试策略** - 对幂等操作增加 RetryCount，对非幂等操作设为 0
5. **身份验证** - 始终使用安全的认证机制，避免在代码中硬编码密钥
6. **响应转换** - 对复杂 API 响应使用 ResponseTransformer 简化数据
7. **标签使用** - 使用有意义的标签便于服务发现

## 扩展

### 自定义认证提供程序

```csharp
public class CustomAuthProvider : IAuthenticationProvider
{
    public string Description => "Custom Auth";
    
    public Task ApplyAsync(HttpRequestMessage request)
    {
        // 实现自定义认证逻辑
        return Task.CompletedTask;
    }
}
```

### 自定义响应转换器

```csharp
public class CustomResponseTransformer : IResponseTransformer
{
    public Task<string> TransformAsync(
        int statusCode,
        string content,
        IReadOnlyDictionary<string, string> headers)
    {
        // 实现自定义转换逻辑
        return Task.FromResult(content);
    }
}
```

## 安全考虑

- **API 密钥管理** - 将 API 密钥存储在配置或密钥管理系统中，不要硬编码
- **HTTPS 只** - 所有请求都通过 HTTPS 发送
- **请求签名** - 对于需要签名的服务，使用适当的签名机制
- **超时保护** - 设置合理的超时时间防止悬挂连接
- **速率限制** - 考虑实现速率限制逻辑
