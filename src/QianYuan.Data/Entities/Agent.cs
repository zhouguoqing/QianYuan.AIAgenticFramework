namespace QianYuan.Data.Entities;

/// <summary>
/// Agent 模型
/// </summary>
public class Agent
{
    /// <summary>
    /// Agent 唯一标识符
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Agent 显示名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Agent 描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 使用的 LLM Provider ID
    /// </summary>
    public string DefaultProviderId { get; set; } = "openai";

    /// <summary>
    /// 使用的模型
    /// </summary>
    public string DefaultModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// 系统提示词
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Agent 配置（JSON）
    /// </summary>
    public string? Configuration { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;

    // Navigation properties
    public ICollection<AgentSkill> Skills { get; set; } = [];
    public ICollection<AgentMcpServer> McpServers { get; set; } = [];
    public ICollection<AgentCliService> CliServices { get; set; } = [];
    public ICollection<AgentTestSession> TestSessions { get; set; } = [];
}

/// <summary>
/// Agent 关联的 Skill
/// </summary>
public class AgentSkill
{
    public int Id { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string SkillId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public Agent Agent { get; set; } = null!;
}

/// <summary>
/// Agent 关联的 MCP Server
/// </summary>
public class AgentMcpServer
{
    public int Id { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string McpServerId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string? Arguments { get; set; } // JSON array
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public Agent Agent { get; set; } = null!;
}

/// <summary>
/// Agent 关联的 CLI 服务
/// </summary>
public class AgentCliService
{
    public int Id { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string CliServiceId { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string BaseUri { get; set; } = string.Empty;
    
    /// <summary>
    /// 认证信息（加密存储）
    /// </summary>
    public string? EncryptedAuthConfig { get; set; }
    
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public Agent Agent { get; set; } = null!;
}

/// <summary>
/// Agent 测试会话
/// </summary>
public class AgentTestSession
{
    public string Id { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    
    /// <summary>
    /// 会话标题
    /// </summary>
    public string Title { get; set; } = string.Empty;
    
    /// <summary>
    /// 会话消息（JSON Array）
    /// </summary>
    public string Messages { get; set; } = "[]";
    
    /// <summary>
    /// 总 token 使用量
    /// </summary>
    public int TotalTokens { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public Agent Agent { get; set; } = null!;
}
