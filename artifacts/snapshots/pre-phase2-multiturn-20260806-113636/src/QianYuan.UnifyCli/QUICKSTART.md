# UnifyCli 快速开始指南

## 📌 概述

QianYuan.UnifyCli 提供了一个统一的方式来将任何 HTTPS 服务或 REST API 封装为 CLI 方法，然后通过 Skill 系统在 Agent 中使用。

## 🚀 5 分钟快速开始

### 第 1 步：在 DI 中注册 UnifyCli

```csharp
using QianYuan.UnifyCli;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// 添加 UnifyCli 基础设施
services.AddUnifyCli();

var sp = services.BuildServiceProvider();
```

### 第 2 步：创建 CLI 方法

```csharp
using QianYuan.UnifyCli.Implementation;
using System.Text.Json;

// 定义一个获取用户信息的 CLI 方法
var getUserMethod = new CliMethodDefinition
{
    Id = "get_user",
    Name = "Get User Info",
    Description = "Get information about a user by ID",
    BaseUri = "https://api.example.com",
    HttpMethod = "GET",
    PathTemplate = "/v1/users/{userId}",
    ParametersSchema = JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new
        {
            userId = new { type = "string", description = "User ID" }
        },
        required = new[] { "userId" }
    }),
    Tags = new[] { "user", "profile" }
};
```

### 第 3 步：创建 CLI 服务并注册方法

```csharp
var userService = new CliServiceDefinition
{
    Id = "user.api",
    Name = "User Service",
    Description = "API for user management",
    BaseUri = "https://api.example.com"
};

userService.RegisterMethod(getUserMethod);

// 在 DI 中注册
services.AddCliService(userService);
```

### 第 4 步：调用 CLI 方法

```csharp
var registry = sp.GetRequiredService<ICliServiceRegistry>();
var service = await registry.GetAsync("user.api");

if (service != null)
{
    var result = await service.InvokeAsync(
        "get_user",
        @"{""userId"": ""12345""}"
    );
    
    Console.WriteLine($"Success: {!result.IsError}");
    Console.WriteLine($"Result: {result.JsonContent}");
}
```

### 第 5 步：（可选）将 CLI 服务暴露为 Skill

```csharp
var skillFactory = sp.GetRequiredService<CliServiceSkillFactory>();
var skill = await skillFactory.CreateSkillAsync("user.api");

// 现在这个 Skill 可以被 Agent 使用
// 每个 CLI 方法都会成为一个工具
```

---

## 🔐 使用认证

### Basic Authentication

```csharp
var authOptions = new AuthenticationOptions
{
    Type = "basic",
    Username = "user@example.com",
    Password = "password123"
};

var authFactory = new AuthenticationProviderFactory();
service.DefaultAuthenticationProvider = authFactory.Create(authOptions);
```

### Bearer Token (JWT/OAuth2)

```csharp
var authOptions = new AuthenticationOptions
{
    Type = "bearer",
    Token = "eyJhbGciOiJIUzI1NiIs..."
};

var authFactory = new AuthenticationProviderFactory();
service.DefaultAuthenticationProvider = authFactory.Create(authOptions);
```

### API Key (Header)

```csharp
var authOptions = new AuthenticationOptions
{
    Type = "api_key",
    Token = "sk-123456789",
    HeaderName = "X-API-Key"
};

var authFactory = new AuthenticationProviderFactory();
service.DefaultAuthenticationProvider = authFactory.Create(authOptions);
```

### API Key (Query Parameter)

```csharp
var authOptions = new AuthenticationOptions
{
    Type = "api_key",
    Token = "sk-123456789",
    QueryParamName = "api_key"
};

var authFactory = new AuthenticationProviderFactory();
service.DefaultAuthenticationProvider = authFactory.Create(authOptions);
```

---

## 📝 常见使用模式

### 模式 1：简单的 GET 请求

```csharp
var method = new CliMethodDefinition
{
    Id = "list_items",
    HttpMethod = "GET",
    PathTemplate = "/v1/items",
    BaseUri = "https://api.example.com",
    QueryParams = new Dictionary<string, string>
    {
        { "limit", "$limit" },
        { "offset", "$offset" }
    },
    ParametersSchema = JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new
        {
            limit = new { type = "integer", description = "Number of items" },
            offset = new { type = "integer", description = "Offset" }
        }
    })
};
```

### 模式 2：POST 请求带 JSON 体

```csharp
var method = new CliMethodDefinition
{
    Id = "create_item",
    HttpMethod = "POST",
    PathTemplate = "/v1/items",
    BaseUri = "https://api.example.com",
    RequestBodyTemplate = "$.",  // 发送整个参数作为 JSON 体
    RequestHeaders = new Dictionary<string, string>
    {
        { "Content-Type", "application/json" }
    },
    ParametersSchema = JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new
        {
            name = new { type = "string" },
            description = new { type = "string" }
        },
        required = new[] { "name" }
    })
};
```

### 模式 3：带路径参数的 DELETE 请求

```csharp
var method = new CliMethodDefinition
{
    Id = "delete_item",
    HttpMethod = "DELETE",
    PathTemplate = "/v1/items/{itemId}",
    BaseUri = "https://api.example.com",
    ParametersSchema = JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new
        {
            itemId = new { type = "string", description = "Item ID to delete" }
        },
        required = new[] { "itemId" }
    })
};
```

### 模式 4：响应转换

```csharp
// 如果 API 返回复杂结构，可以提取特定字段
var transformer = new JsonPathResponseTransformer("$.data.items");

var method = new CliMethodDefinition
{
    // ... 其他配置
    ResponseTransformer = transformer
};

// 原始响应：{ "data": { "items": [...] }, "meta": {...} }
// 转换后的结果：[...]
```

---

## 🛠️ 高级配置

### 设置超时和重试

```csharp
var method = new CliMethodDefinition
{
    // ...
    TimeoutMs = 30000,      // 30 秒超时
    RetryCount = 3,         // 失败时重试 3 次
    RetryDelayMs = 500      // 每次重试延迟 500ms
};
```

### 服务发现和搜索

```csharp
var registry = sp.GetRequiredService<ICliServiceRegistry>();

// 列出所有已注册的服务
var allServices = registry.ListManifests();

// 按关键字搜索
var searchResults = await registry.SearchAsync(new[] { "user", "profile" });

// 搜索结果包含标签或名称与关键字匹配的服务
```

---

## 📊 完整示例

请参考 [UnifyCliIntegrationExample.cs](../../samples/QianYuan.Sample.Console/UnifyCliIntegrationExample.cs)，其中包含：

1. ✓ 基本使用示例
2. ✓ 认证示例
3. ✓ 注册表和发现示例
4. ✓ DI 集成示例
5. ✓ Skill 集成示例
6. ✓ 错误处理示例
7. ✓ 完整应用配置示例

---

## 🎯 常见问题

### Q: 如何处理 API 的速率限制？

A: 使用 `RetryCount` 和 `RetryDelayMs` 来配置重试策略。对于需要精细控制的情况，可以创建自定义的 HTTP 客户端。

### Q: 如何安全地存储 API 密钥？

A: 不要在代码中硬编码密钥。使用以下方式之一：
- 环境变量
- 用户密钥存储
- 密钥管理服务（Azure Key Vault, AWS Secrets Manager 等）
- 配置文件

### Q: 是否支持自定义认证？

A: 是的。实现 `IAuthenticationProvider` 接口创建自定义认证。

### Q: 如何处理需要多步认证的 API（如 OAuth2）？

A: 在应用启动时获取 token，然后使用 Bearer Token 认证。或者在 CLI 方法的 `RequestHeaders` 中添加动态 header。

---

## 📚 更多资源

- [完整 API 文档](README.md)
- [示例服务实现](./Examples/ServiceExamples.cs)
- [核心接口定义](./Abstractions/)
