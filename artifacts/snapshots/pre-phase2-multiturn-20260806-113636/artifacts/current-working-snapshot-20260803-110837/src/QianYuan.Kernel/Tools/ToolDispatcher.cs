using Microsoft.Extensions.Logging;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Exceptions;
using QianYuan.Core.Models;

namespace QianYuan.Kernel.Tools;

/// <summary>
/// Default <see cref="IToolDispatcher"/> - routes a tool call to the owning skill via the SkillManager,
/// using the per-tool <see cref="ToolDefinition.SkillId"/> tag set by the skill manager when tools were collected.
/// </summary>
public sealed class ToolDispatcher : IToolDispatcher
{
    private readonly ISkillManager _skills;
    private readonly IAgentRegistry _agents;
    private readonly ILogger<ToolDispatcher> _logger;

    // Provided externally: the live snapshot of "which tools are currently mounted for this agent turn"
    // built by the ReAct engine before each LLM call. Keyed by tool name -> skill id.
    private readonly Func<string, string?> _resolveSkillForTool;

    public ToolDispatcher(
        ISkillManager skills,
        IAgentRegistry agents,
        ILogger<ToolDispatcher> logger,
        Func<string, string?> resolveSkillForTool)
    {
        _skills = skills;
        _agents = agents;
        _logger = logger;
        _resolveSkillForTool = resolveSkillForTool;
    }

    public async ValueTask<SkillInvocationResult> InvokeAsync(
        string toolName,
        string argumentsJson,
        SkillInvocationContext context,
        CancellationToken ct = default)
    {
        // Agent-as-tool delegation: tool name "agent.<id>" routes to another agent.
        if (toolName.StartsWith("agent.", StringComparison.OrdinalIgnoreCase))
        {
            var agentId = toolName["agent.".Length..];
            var agent = _agents.Get(agentId) ?? throw new AgentNotFoundException(agentId);
            return await DelegateToAgentAsync(agent, argumentsJson, context, ct).ConfigureAwait(false);
        }

        var skillId = _resolveSkillForTool(toolName)
            ?? throw new ToolNotFoundException(toolName);

        var skill = await _skills.GetAsync(skillId, ct).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("Dispatching tool {Tool} -> skill {Skill}", toolName, skillId);
            return await skill.InvokeAsync(toolName, argumentsJson, context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {Tool} failed", toolName);
            return SkillInvocationResult.Error(ex.Message);
        }
    }

    private static async ValueTask<SkillInvocationResult> DelegateToAgentAsync(
        IAgent agent, string argumentsJson, SkillInvocationContext context, CancellationToken ct)
    {
        // Expected payload: { "input": "..." }
        string input;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
            input = doc.RootElement.TryGetProperty("input", out var v) ? (v.GetString() ?? "") : argumentsJson;
        }
        catch
        {
            input = argumentsJson;
        }

        var run = new AgentRunRequest
        {
            Messages = [ChatMessage.User(input)],
            SessionId = context.SessionId,
        };

        var sb = new System.Text.StringBuilder();
        await foreach (var chunk in agent.RunAsync(run, ct).ConfigureAwait(false))
        {
            if (chunk.Kind == Core.Streaming.StreamingChunkKind.TextDelta && chunk.Text is not null)
                sb.Append(chunk.Text);
            else if (chunk.Kind == Core.Streaming.StreamingChunkKind.Error)
                return SkillInvocationResult.Error(chunk.Text ?? "agent error");
        }

        var json = System.Text.Json.JsonSerializer.Serialize(new { output = sb.ToString() });
        return SkillInvocationResult.Ok(json, sb.ToString());
    }
}
