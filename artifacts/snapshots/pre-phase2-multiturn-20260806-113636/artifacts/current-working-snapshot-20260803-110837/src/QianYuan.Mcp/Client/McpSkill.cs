using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;

namespace QianYuan.Mcp.Client;

/// <summary>
/// Adapts a connected <see cref="IMcpClient"/> into an <see cref="ISkill"/> so that an external MCP server's
/// tools become first-class, progressively-loadable tools in the QianYuan kernel.
///
/// Tool names are namespaced as "mcp.&lt;serverId&gt;.&lt;toolName&gt;" to avoid collisions across servers.
/// </summary>
public sealed class McpSkill : ISkill
{
    private readonly IMcpClient _client;
    private readonly string _idPrefix;
    private IReadOnlyList<ToolDefinition>? _cached;

    public McpSkill(IMcpClient client)
    {
        _client = client;
        _idPrefix = $"mcp.{client.ServerId}";
    }

    public string Id => _idPrefix;
    public string Name => $"MCP: {_client.ServerId}";
    public string Description => $"Tools provided by external MCP server '{_client.ServerId}'.";
    public IReadOnlyList<string> Tags => new[] { "mcp", _client.ServerId };
    public string? SystemPromptFragment => null;

    public async ValueTask<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;
        if (!_client.IsConnected) await _client.ConnectAsync(ct).ConfigureAwait(false);

        var mcpTools = await _client.ListToolsAsync(ct).ConfigureAwait(false);
        _cached = mcpTools.Select(t => new ToolDefinition
        {
            Name = $"{_idPrefix}.{t.Name}",
            Description = t.Description,
            JsonSchema = t.JsonSchema,
            SkillId = Id,
        }).ToArray();
        return _cached;
    }

    public async ValueTask<SkillInvocationResult> InvokeAsync(
        string toolName, string argumentsJson, SkillInvocationContext context, CancellationToken ct = default)
    {
        var bareName = toolName.StartsWith(_idPrefix + ".", StringComparison.Ordinal)
            ? toolName[(_idPrefix.Length + 1)..]
            : toolName;

        if (!_client.IsConnected) await _client.ConnectAsync(ct).ConfigureAwait(false);
        var result = await _client.CallToolAsync(bareName, argumentsJson, ct).ConfigureAwait(false);
        return result.IsError
            ? SkillInvocationResult.Error(result.Text ?? "MCP tool error")
            : SkillInvocationResult.Ok(result.JsonContent, result.Text);
    }
}
