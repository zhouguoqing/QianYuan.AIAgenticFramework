using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Memory;
using QianYuan.Core.Models;
using QianYuan.Core.Streaming;

namespace QianYuan.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ChatController : ControllerBase
{
    private readonly IAgentRegistry _agents;
    private readonly ILlmProviderRegistry _providers;
    private readonly ISessionStore _sessions;
    private readonly ILogger<ChatController> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ChatController(IAgentRegistry agents, ILlmProviderRegistry providers, ISessionStore sessions, ILogger<ChatController> logger)
    {
        _agents = agents; _providers = providers; _sessions = sessions; _logger = logger;
    }

    /// <summary>SSE streaming chat. Each event has a "kind" and a payload matching <see cref="StreamingChunk"/>.</summary>
    [HttpPost("stream")]
    public async Task Stream([FromBody] ChatStreamRequest req, CancellationToken ct)
    {
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache, no-transform";
        Response.Headers["X-Accel-Buffering"] = "no";

        var agent = _agents.Get(req.AgentId ?? "qianyuan.default")
                    ?? _agents.List().FirstOrDefault();
        if (agent is null)
        {
            await WriteSse("error", new { message = "no agents registered" }, ct);
            return;
        }

        var sessionId = req.SessionId ?? Guid.NewGuid().ToString("N");
        var state = await _sessions.GetAsync(sessionId, ct).ConfigureAwait(false)
                    ?? new SessionState { SessionId = sessionId, AgentId = agent.Id, OwnerId = req.OwnerId };

        var messages = state.Messages.ToList();
        messages.Add(BuildUserMessage(req));
        state.Messages.Clear();
        state.Messages.AddRange(messages);
        state.Title ??= req.UserText is { Length: > 0 } ? Snippet(req.UserText, 40) : null;
        state.AgentId = agent.Id;

        var assistantText = new System.Text.StringBuilder();
        await WriteSse("session", new { sessionId, agentId = agent.Id }, ct);

        var providerOverride = NormalizeAuto(req.Provider);
        var modelOverride = NormalizeAuto(req.Model);
        var resolvedProvider = providerOverride is null ? _providers.Default : _providers.Get(providerOverride);
        if (resolvedProvider is null)
        {
            await WriteSse("error", new { message = $"云端大模型服务 '{providerOverride}' 未注册或未启用。" }, ct);
            return;
        }

        await WriteSse("runtime", new
        {
            modelSource = "cloud",
            provider = resolvedProvider.ProviderId,
            model = modelOverride ?? resolvedProvider.DefaultModel,
        }, ct);

        var run = new AgentRunRequest
        {
            Messages = messages,
            SessionId = sessionId,
            ModelOverride = modelOverride,
            ProviderOverride = providerOverride,
            SystemPromptOverride = string.IsNullOrWhiteSpace(req.SystemPrompt) ? null : req.SystemPrompt,
            PreloadSkills = req.Skills,
            MaxIterations = req.MaxIterations,
            Metadata = BuildMetadata(req, resolvedProvider.ProviderId, modelOverride ?? resolvedProvider.DefaultModel),
        };

        try
        {
            await foreach (var chunk in agent.RunAsync(run, ct).ConfigureAwait(false))
            {
                if (chunk.Kind == StreamingChunkKind.TextDelta && chunk.Text is not null)
                    assistantText.Append(chunk.Text);

                await WriteSse(SseEventName(chunk.Kind), SerializeChunk(chunk), ct);
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "chat/stream failed");
            await WriteSse("error", new { message = ex.Message }, ct);
        }

        if (assistantText.Length > 0)
            state.Messages.Add(ChatMessage.Assistant(assistantText.ToString()));
        await _sessions.SaveAsync(state, ct).ConfigureAwait(false);

        await WriteSse("done", new { sessionId }, ct);
    }

    private async Task WriteSse(string eventName, object data, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(data, JsonOpts);
        var bytes = System.Text.Encoding.UTF8.GetBytes($"event: {eventName}\ndata: {payload}\n\n");
        await Response.Body.WriteAsync(bytes, ct).ConfigureAwait(false);
        await Response.Body.FlushAsync(ct).ConfigureAwait(false);
    }

    private static ChatMessage BuildUserMessage(ChatStreamRequest req)
    {
        var parts = new List<ContentPart>();
        if (!string.IsNullOrEmpty(req.UserText)) parts.Add(ContentPart.FromText(req.UserText));
        if (req.Images is not null)
            foreach (var img in req.Images)
            {
                if (!string.IsNullOrEmpty(img.Url)) parts.Add(ContentPart.FromImageUrl(img.Url, img.Mime));
                else if (!string.IsNullOrEmpty(img.Base64)) parts.Add(ContentPart.FromImageBase64(img.Base64, img.Mime ?? "image/png"));
            }
        if (parts.Count == 0) parts.Add(ContentPart.FromText(""));
        return new ChatMessage { Role = ChatRole.User, Parts = parts };
    }

    private static string SseEventName(StreamingChunkKind k) => k switch
    {
        StreamingChunkKind.TextDelta => "text",
        StreamingChunkKind.ThinkingDelta => "thinking",
        StreamingChunkKind.ToolCallStart => "tool_call_start",
        StreamingChunkKind.ToolCallArgsDelta => "tool_call_args",
        StreamingChunkKind.ToolCallEnd => "tool_call_end",
        StreamingChunkKind.ToolObservation => "tool_observation",
        StreamingChunkKind.Usage => "usage",
        StreamingChunkKind.Start => "start",
        StreamingChunkKind.End => "end",
        StreamingChunkKind.Warning => "warning",
        StreamingChunkKind.Error => "error",
        _ => "chunk"
    };

    private static object SerializeChunk(StreamingChunk c) => new
    {
        kind = c.Kind.ToString(),
        text = c.Text,
        toolCallId = c.ToolCallId,
        toolName = c.ToolName,
        toolArgsJson = c.ToolArgsJson,
        finishReason = c.FinishReason,
        model = c.Model,
        agentId = c.AgentId,
        skillId = c.SkillId,
        step = c.Step,
        usage = c.Usage is null ? null : new
        {
            input = c.Usage.InputTokens,
            output = c.Usage.OutputTokens,
            cacheRead = c.Usage.CacheReadTokens,
            cacheWrite = c.Usage.CacheWriteTokens,
        }
    };

    private static string Snippet(string s, int n) => s.Length <= n ? s : s[..n] + "...";

    private static string? NormalizeAuto(string? value)
        => string.IsNullOrWhiteSpace(value) || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();

    private static Dictionary<string, string> BuildMetadata(ChatStreamRequest req, string providerId, string model)
    {
        var metadata = new Dictionary<string, string>
        {
            ["modelSource"] = "cloud",
            ["provider"] = providerId,
            ["model"] = model,
        };

        if (!string.IsNullOrWhiteSpace(req.OwnerId)) metadata["ownerId"] = req.OwnerId;
        if (!string.IsNullOrWhiteSpace(req.WorkspaceId)) metadata["workspaceId"] = req.WorkspaceId;
        if (!string.IsNullOrWhiteSpace(req.WorkspacePath)) metadata["workspacePath"] = req.WorkspacePath;
        if (!string.IsNullOrWhiteSpace(req.WorkspaceLabel)) metadata["workspaceLabel"] = req.WorkspaceLabel;
        if (!string.IsNullOrWhiteSpace(req.Permission)) metadata["permission"] = req.Permission;

        return metadata;
    }
}

public sealed class ChatStreamRequest
{
    public string? AgentId { get; set; }
    public string? SessionId { get; set; }
    public string? OwnerId { get; set; }
    public string? UserText { get; set; }
    public ImagePart[]? Images { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string[]? Skills { get; set; }
    public int? MaxIterations { get; set; }
    public string? SystemPrompt { get; set; }
    public string? WorkspaceId { get; set; }
    public string? WorkspacePath { get; set; }
    public string? WorkspaceLabel { get; set; }
    public string? Permission { get; set; }
}

public sealed class ImagePart
{
    public string? Url { get; set; }
    public string? Base64 { get; set; }
    public string? Mime { get; set; }
}
