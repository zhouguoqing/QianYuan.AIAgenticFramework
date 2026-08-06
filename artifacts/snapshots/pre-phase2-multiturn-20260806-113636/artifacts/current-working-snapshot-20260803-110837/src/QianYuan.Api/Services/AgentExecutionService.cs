using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Exceptions;
using QianYuan.Core.Models;
using QianYuan.Core.Streaming;
using QianYuan.Data.Repositories;
using QianYuan.Data.Services;
using QianYuan.Kernel;
using QianYuan.Kernel.Skills;

namespace QianYuan.Api.Services;

/// <summary>
/// 测试消息
/// </summary>
public class TestMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty; // "user", "assistant", "tool"

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("toolName")]
    public string? ToolName { get; set; }

    [JsonPropertyName("toolCallId")]
    public string? ToolCallId { get; set; }
}

/// <summary>
/// 工具调用请求
/// </summary>
public class ToolCallRequest
{
    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "{}";
}

/// <summary>
/// Agent 执行服务接口
/// </summary>
public interface IAgentExecutionService
{
    Task<string> CallToolAsync(string agentId, ToolCallRequest request, CancellationToken ct = default);
    IAsyncEnumerable<string> InteractAsync(string agentId, string userMessage, CancellationToken ct = default);
    Task<List<object>> GetToolsAsync(string agentId, CancellationToken ct = default);
}

/// <summary>
/// Agent 执行服务实现
/// </summary>
public class AgentExecutionService : IAgentExecutionService
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly ILlmProviderRegistry _providerRegistry;
    private readonly ISkillManager _skillManager;
    private readonly IAgentRepository _repository;
    private readonly IEncryptionService _encryptionService;
    private readonly IServiceProvider _services;
    private readonly ILogger<AgentExecutionService> _logger;

    public AgentExecutionService(
        IAgentRegistry agentRegistry,
        ILlmProviderRegistry providerRegistry,
        ISkillManager skillManager,
        IAgentRepository repository,
        IEncryptionService encryptionService,
        IServiceProvider services,
        ILogger<AgentExecutionService> logger)
    {
        _agentRegistry = agentRegistry;
        _providerRegistry = providerRegistry;
        _skillManager = skillManager;
        _repository = repository;
        _encryptionService = encryptionService;
        _services = services;
        _logger = logger;
    }

    public async Task<string> CallToolAsync(string agentId, ToolCallRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Calling tool '{ToolName}' on agent '{AgentId}'", request.ToolName, agentId);

        var agent = await _repository.GetByIdAsync(agentId, ct);
        if (agent == null)
            throw new KeyNotFoundException($"Agent '{agentId}' not found");

        var mountedSkillIds = agent.Skills
            .Where(skill => skill.Enabled)
            .OrderByDescending(skill => skill.Priority)
            .Select(skill => skill.SkillId)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var skillId in mountedSkillIds)
        {
            ISkill skill;
            try
            {
                skill = await _skillManager.GetAsync(skillId, ct);
            }
            catch (SkillNotFoundException)
            {
                _logger.LogWarning("Mounted skill '{SkillId}' was not found for agent '{AgentId}'", skillId, agentId);
                continue;
            }

            var tools = await skill.GetToolsAsync(ct);
            if (tools.FirstOrDefault(t => t.Name == request.ToolName) is ToolDefinition tool)
            {
                var context = new SkillInvocationContext
                {
                    AgentId = agentId,
                    SessionId = $"agent-store-test-{Guid.NewGuid():N}",
                    Services = _services,
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = "agent-store",
                        ["skillId"] = skillId,
                        ["toolName"] = tool.Name
                    }
                };

                var result = await skill.InvokeAsync(
                    request.ToolName,
                    request.Arguments,
                    context,
                    ct);

                return result.JsonContent;
            }
        }

        throw new KeyNotFoundException($"Tool '{request.ToolName}' not found in mounted skills for agent '{agentId}'");
    }

    public async IAsyncEnumerable<string> InteractAsync(
        string agentId,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation("Interacting with agent '{AgentId}'", agentId);

        var agent = await _repository.GetByIdAsync(agentId, ct);
        if (agent == null)
        {
            yield return JsonSerializer.Serialize(new { type = "error", content = $"Agent '{agentId}' not found" });
            yield break;
        }

        var provider = _providerRegistry.Get(agent.DefaultProviderId);
        if (provider == null)
        {
            yield return JsonSerializer.Serialize(new { type = "error", content = $"Provider '{agent.DefaultProviderId}' not found" });
            yield break;
        }

        var chatRequest = new ChatRequest
        {
            Messages = new[]
            {
                ChatMessage.System(agent.SystemPrompt ?? "You are a helpful assistant."),
                ChatMessage.User(userMessage)
            },
            Options = new GenerationOptions
            {
                Model = agent.DefaultModel,
                MaxOutputTokens = 2000,
                Stream = true
            }
        };

        await foreach (var chunk in provider.StreamAsync(chatRequest, ct))
        {
            if (chunk.Kind == StreamingChunkKind.TextDelta && !string.IsNullOrEmpty(chunk.Text))
            {
                yield return JsonSerializer.Serialize(new
                {
                    type = "text",
                    content = chunk.Text
                });
            }
            else if (chunk.Kind == StreamingChunkKind.Error && !string.IsNullOrEmpty(chunk.Text))
            {
                yield return JsonSerializer.Serialize(new
                {
                    type = "error",
                    content = chunk.Text
                });
            }
        }
    }

    public async Task<List<object>> GetToolsAsync(string agentId, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting tools for agent '{AgentId}'", agentId);

        var agent = await _repository.GetByIdAsync(agentId, ct);
        if (agent == null)
            throw new KeyNotFoundException($"Agent '{agentId}' not found");

        var tools = new List<object>();
        var mountedSkillIds = agent.Skills
            .Where(skill => skill.Enabled)
            .OrderByDescending(skill => skill.Priority)
            .Select(skill => skill.SkillId)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var skillId in mountedSkillIds)
        {
            ISkill skill;
            try
            {
                skill = await _skillManager.GetAsync(skillId, ct);
            }
            catch (SkillNotFoundException)
            {
                _logger.LogWarning("Mounted skill '{SkillId}' was not found for agent '{AgentId}'", skillId, agentId);
                continue;
            }

            var skillTools = await skill.GetToolsAsync(ct);
            foreach (var tool in skillTools)
            {
                tools.Add(new
                {
                    name = tool.Name,
                    description = tool.Description,
                    jsonSchema = tool.JsonSchema,
                    skillId = tool.SkillId ?? skillId
                });
            }
        }

        return tools;
    }
}
