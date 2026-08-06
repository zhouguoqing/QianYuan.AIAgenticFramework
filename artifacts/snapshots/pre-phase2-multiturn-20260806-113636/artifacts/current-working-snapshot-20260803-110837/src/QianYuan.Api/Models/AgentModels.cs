using System.Text.Json.Serialization;

namespace QianYuan.Api.Models;

/// <summary>
/// Agent 创建/编辑请求
/// </summary>
public class CreateAgentRequest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("defaultProviderId")]
    public string? DefaultProviderId { get; set; }

    [JsonPropertyName("defaultModel")]
    public string? DefaultModel { get; set; }

    [JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; set; }
}

/// <summary>
/// Agent 响应
/// </summary>
public class AgentResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("defaultProviderId")]
    public string DefaultProviderId { get; set; } = string.Empty;

    [JsonPropertyName("defaultModel")]
    public string DefaultModel { get; set; } = string.Empty;

    [JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; set; }

    [JsonPropertyName("skills")]
    public List<AgentSkillResponse> Skills { get; set; } = [];

    [JsonPropertyName("mcpServers")]
    public List<AgentMcpServerResponse> McpServers { get; set; } = [];

    [JsonPropertyName("cliServices")]
    public List<AgentCliServiceResponse> CliServices { get; set; } = [];

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

/// <summary>
/// Agent Skill 响应
/// </summary>
public class AgentSkillResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("skillId")]
    public string SkillId { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; }
}

/// <summary>
/// Agent MCP Server 响应
/// </summary>
public class AgentMcpServerResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("mcpServerId")]
    public string McpServerId { get; set; } = string.Empty;

    [JsonPropertyName("serverName")]
    public string ServerName { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

/// <summary>
/// Agent CLI Service 响应
/// </summary>
public class AgentCliServiceResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("cliServiceId")]
    public string CliServiceId { get; set; } = string.Empty;

    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = string.Empty;

    [JsonPropertyName("baseUri")]
    public string BaseUri { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

/// <summary>
/// 添加 Skill 到 Agent 的请求
/// </summary>
public class AddSkillToAgentRequest
{
    [JsonPropertyName("skillId")]
    public string SkillId { get; set; } = string.Empty;

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;
}

/// <summary>
/// 添加 MCP Server 到 Agent 的请求
/// </summary>
public class AddMcpServerToAgentRequest
{
    [JsonPropertyName("mcpServerId")]
    public string McpServerId { get; set; } = string.Empty;

    [JsonPropertyName("serverName")]
    public string ServerName { get; set; } = string.Empty;

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string[]? Arguments { get; set; }
}

/// <summary>
/// 添加 CLI Service 到 Agent 的请求
/// </summary>
public class AddCliServiceToAgentRequest
{
    [JsonPropertyName("cliServiceId")]
    public string CliServiceId { get; set; } = string.Empty;

    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = string.Empty;

    [JsonPropertyName("baseUri")]
    public string BaseUri { get; set; } = string.Empty;

    [JsonPropertyName("authConfig")]
    public object? AuthConfig { get; set; }
}
