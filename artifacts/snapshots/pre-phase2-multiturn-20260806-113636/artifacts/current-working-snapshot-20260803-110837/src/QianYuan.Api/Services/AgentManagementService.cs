using System.Text.Json;
using System.Text.Json.Serialization;
using QianYuan.Api.Models;
using QianYuan.Data.Entities;
using QianYuan.Data.Repositories;
using QianYuan.Data.Services;

namespace QianYuan.Api.Services;

/// <summary>
/// Agent 管理服务
/// </summary>
public interface IAgentManagementService
{
    Task<AgentResponse?> GetAgentAsync(string agentId, CancellationToken ct = default);
    Task<List<AgentResponse>> GetAllAgentsAsync(CancellationToken ct = default);
    Task<AgentResponse> CreateAgentAsync(CreateAgentRequest request, CancellationToken ct = default);
    Task<AgentResponse> UpdateAgentAsync(string agentId, CreateAgentRequest request, CancellationToken ct = default);
    Task DeleteAgentAsync(string agentId, CancellationToken ct = default);
    Task<AgentSkillResponse> AddSkillAsync(string agentId, AddSkillToAgentRequest request, CancellationToken ct = default);
    Task RemoveSkillAsync(string agentId, int skillId, CancellationToken ct = default);
    Task<AgentMcpServerResponse> AddMcpServerAsync(string agentId, AddMcpServerToAgentRequest request, CancellationToken ct = default);
    Task RemoveMcpServerAsync(string agentId, int serverId, CancellationToken ct = default);
    Task<AgentCliServiceResponse> AddCliServiceAsync(string agentId, AddCliServiceToAgentRequest request, CancellationToken ct = default);
    Task RemoveCliServiceAsync(string agentId, int serviceId, CancellationToken ct = default);
}

/// <summary>
/// Agent 管理服务实现
/// </summary>
public class AgentManagementService : IAgentManagementService
{
    private readonly IAgentRepository _repository;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<AgentManagementService> _logger;

    public AgentManagementService(
        IAgentRepository repository,
        IEncryptionService encryptionService,
        ILogger<AgentManagementService> logger)
    {
        _repository = repository;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<AgentResponse?> GetAgentAsync(string agentId, CancellationToken ct = default)
    {
        var agent = await _repository.GetByIdAsync(agentId, ct);
        return agent == null ? null : MapToResponse(agent);
    }

    public async Task<List<AgentResponse>> GetAllAgentsAsync(CancellationToken ct = default)
    {
        var agents = await _repository.GetAllAsync(ct);
        return agents.Select(MapToResponse).ToList();
    }

    public async Task<AgentResponse> CreateAgentAsync(CreateAgentRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Id))
            throw new ArgumentException("Agent ID cannot be empty");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Agent name cannot be empty");

        var agent = new Agent
        {
            Id = request.Id.ToLowerInvariant(),
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            DefaultProviderId = request.DefaultProviderId ?? "openai",
            DefaultModel = request.DefaultModel ?? "gpt-4o-mini",
            SystemPrompt = request.SystemPrompt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        agent = await _repository.CreateAsync(agent, ct);
        _logger.LogInformation("Agent '{AgentId}' created", agent.Id);

        return MapToResponse(agent);
    }

    public async Task<AgentResponse> UpdateAgentAsync(string agentId, CreateAgentRequest request, CancellationToken ct = default)
    {
        var agent = await _repository.GetByIdAsync(agentId, ct);
        if (agent == null)
            throw new KeyNotFoundException($"Agent '{agentId}' not found");

        agent.Name = request.Name ?? agent.Name;
        agent.Description = request.Description ?? agent.Description;
        agent.DefaultProviderId = request.DefaultProviderId ?? agent.DefaultProviderId;
        agent.DefaultModel = request.DefaultModel ?? agent.DefaultModel;
        agent.SystemPrompt = request.SystemPrompt ?? agent.SystemPrompt;
        agent.UpdatedAt = DateTime.UtcNow;

        agent = await _repository.UpdateAsync(agent, ct);
        _logger.LogInformation("Agent '{AgentId}' updated", agent.Id);

        return MapToResponse(agent);
    }

    public async Task DeleteAgentAsync(string agentId, CancellationToken ct = default)
    {
        await _repository.DeleteAsync(agentId, ct);
        _logger.LogInformation("Agent '{AgentId}' deleted", agentId);
    }

    public async Task<AgentSkillResponse> AddSkillAsync(string agentId, AddSkillToAgentRequest request, CancellationToken ct = default)
    {
        var agent = await _repository.GetByIdAsync(agentId, ct);
        if (agent == null)
            throw new KeyNotFoundException($"Agent '{agentId}' not found");

        // 检查是否已经添加过
        if (agent.Skills.Any(s => s.SkillId == request.SkillId))
            throw new InvalidOperationException($"Skill '{request.SkillId}' already added to agent");

        var skill = await _repository.AddSkillAsync(agentId, request.SkillId, request.Priority, ct);
        _logger.LogInformation("Skill '{SkillId}' added to agent '{AgentId}'", request.SkillId, agentId);

        return new AgentSkillResponse
        {
            Id = skill.Id,
            SkillId = skill.SkillId,
            Enabled = skill.Enabled,
            Priority = skill.Priority
        };
    }

    public async Task RemoveSkillAsync(string agentId, int skillId, CancellationToken ct = default)
    {
        await _repository.RemoveSkillAsync(agentId, skillId, ct);
        _logger.LogInformation("Skill {SkillId} removed from agent '{AgentId}'", skillId, agentId);
    }

    public async Task<AgentMcpServerResponse> AddMcpServerAsync(string agentId, AddMcpServerToAgentRequest request, CancellationToken ct = default)
    {
        var agent = await _repository.GetByIdAsync(agentId, ct);
        if (agent == null)
            throw new KeyNotFoundException($"Agent '{agentId}' not found");

        var server = new AgentMcpServer
        {
            AgentId = agentId,
            McpServerId = request.McpServerId,
            ServerName = request.ServerName,
            Command = request.Command,
            Arguments = request.Arguments != null ? JsonSerializer.Serialize(request.Arguments) : null,
            CreatedAt = DateTime.UtcNow
        };

        server = await _repository.AddMcpServerAsync(server, ct);
        _logger.LogInformation("MCP Server '{McpServerId}' added to agent '{AgentId}'", request.McpServerId, agentId);

        return new AgentMcpServerResponse
        {
            Id = server.Id,
            McpServerId = server.McpServerId,
            ServerName = server.ServerName,
            Enabled = server.Enabled
        };
    }

    public async Task RemoveMcpServerAsync(string agentId, int serverId, CancellationToken ct = default)
    {
        await _repository.RemoveMcpServerAsync(serverId, ct);
        _logger.LogInformation("MCP Server {ServerId} removed from agent '{AgentId}'", serverId, agentId);
    }

    public async Task<AgentCliServiceResponse> AddCliServiceAsync(string agentId, AddCliServiceToAgentRequest request, CancellationToken ct = default)
    {
        var agent = await _repository.GetByIdAsync(agentId, ct);
        if (agent == null)
            throw new KeyNotFoundException($"Agent '{agentId}' not found");

        // 加密认证配置
        string? encryptedAuthConfig = null;
        if (request.AuthConfig != null)
        {
            var authJson = JsonSerializer.Serialize(request.AuthConfig);
            encryptedAuthConfig = _encryptionService.Encrypt(authJson);
        }

        var service = new AgentCliService
        {
            AgentId = agentId,
            CliServiceId = request.CliServiceId,
            ServiceName = request.ServiceName,
            BaseUri = request.BaseUri,
            EncryptedAuthConfig = encryptedAuthConfig,
            CreatedAt = DateTime.UtcNow
        };

        service = await _repository.AddCliServiceAsync(service, ct);
        _logger.LogInformation("CLI Service '{CliServiceId}' added to agent '{AgentId}'", request.CliServiceId, agentId);

        return new AgentCliServiceResponse
        {
            Id = service.Id,
            CliServiceId = service.CliServiceId,
            ServiceName = service.ServiceName,
            BaseUri = service.BaseUri,
            Enabled = service.Enabled
        };
    }

    public async Task RemoveCliServiceAsync(string agentId, int serviceId, CancellationToken ct = default)
    {
        await _repository.RemoveCliServiceAsync(serviceId, ct);
        _logger.LogInformation("CLI Service {ServiceId} removed from agent '{AgentId}'", serviceId, agentId);
    }

    private static AgentResponse MapToResponse(Agent agent)
    {
        return new AgentResponse
        {
            Id = agent.Id,
            Name = agent.Name,
            Description = agent.Description,
            DefaultProviderId = agent.DefaultProviderId,
            DefaultModel = agent.DefaultModel,
            SystemPrompt = agent.SystemPrompt,
            Enabled = agent.Enabled,
            CreatedAt = agent.CreatedAt,
            UpdatedAt = agent.UpdatedAt,
            Skills = agent.Skills.Select(s => new AgentSkillResponse
            {
                Id = s.Id,
                SkillId = s.SkillId,
                Enabled = s.Enabled,
                Priority = s.Priority
            }).ToList(),
            McpServers = agent.McpServers.Select(m => new AgentMcpServerResponse
            {
                Id = m.Id,
                McpServerId = m.McpServerId,
                ServerName = m.ServerName,
                Enabled = m.Enabled
            }).ToList(),
            CliServices = agent.CliServices.Select(c => new AgentCliServiceResponse
            {
                Id = c.Id,
                CliServiceId = c.CliServiceId,
                ServiceName = c.ServiceName,
                BaseUri = c.BaseUri,
                Enabled = c.Enabled
            }).ToList()
        };
    }
}
