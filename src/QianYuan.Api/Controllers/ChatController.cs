using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Memory;
using QianYuan.Core.Models;
using QianYuan.Core.Streaming;
using QianYuan.Data.Entities;
using QianYuan.Data.Repositories;

namespace QianYuan.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ChatController : ControllerBase
{
    private readonly IAgentRegistry _agents;
    private readonly ILlmProviderRegistry _providers;
    private readonly ISessionStore _sessions;
    private readonly IAgentRepository _agentRepository;
    private readonly ILogger<ChatController> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ChatController(
        IAgentRegistry agents,
        ILlmProviderRegistry providers,
        ISessionStore sessions,
        IAgentRepository agentRepository,
        ILogger<ChatController> logger)
    {
        _agents = agents;
        _providers = providers;
        _sessions = sessions;
        _agentRepository = agentRepository;
        _logger = logger;
    }

    /// <summary>SSE streaming chat. Each event has a "kind" and a payload matching <see cref="StreamingChunk"/>.</summary>
    [HttpPost("stream")]
    public async Task Stream([FromBody] ChatStreamRequest req, CancellationToken ct)
    {
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache, no-transform";
        Response.Headers["X-Accel-Buffering"] = "no";

        var requestedAgentId = req.AgentId ?? "qianyuan.default";
        var agent = _agents.Get(requestedAgentId);
        Agent? storeAgent = null;
        if (agent is null && !string.IsNullOrWhiteSpace(req.AgentId))
        {
            storeAgent = await _agentRepository.GetByIdAsync(req.AgentId, ct).ConfigureAwait(false);
            if (storeAgent?.Enabled == true)
                agent = _agents.Get("qianyuan.default") ?? _agents.List().FirstOrDefault();
        }

        agent ??= _agents.List().FirstOrDefault();
        if (agent is null)
        {
            await WriteSse("error", new { message = "no agents registered" }, ct);
            return;
        }

        var sessionId = req.SessionId ?? Guid.NewGuid().ToString("N");
        var state = await _sessions.GetAsync(sessionId, ct).ConfigureAwait(false)
                    ?? new SessionState { SessionId = sessionId, AgentId = agent.Id, OwnerId = req.OwnerId };

        var messages = state.Messages.ToList();
        if (req.ReuseLastUserMessage)
        {
            if (messages.Count == 0 || messages[^1].Role != ChatRole.User)
            {
                await WriteSse("error", new { message = "reuseLastUserMessage requires the session to end with a user message" }, ct);
                return;
            }
        }
        else
        {
            messages.Add(BuildUserMessage(req));
        }
        state.Messages.Clear();
        state.Messages.AddRange(messages);
        state.Title ??= req.UserText is { Length: > 0 } ? Snippet(req.UserText, 40) : null;
        state.AgentId = agent.Id;

        var transcript = new StreamTranscriptBuilder();
        await WriteSse("session", new { sessionId, agentId = agent.Id }, ct);

        var providerOverride = NormalizeAuto(req.Provider) ?? NormalizeAuto(storeAgent?.DefaultProviderId);
        var modelOverride = NormalizeAuto(req.Model) ?? NormalizeAuto(storeAgent?.DefaultModel);
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
            SystemPromptOverride = BuildSystemPromptOverride(storeAgent, req.SystemPrompt),
            PreloadSkills = BuildPreloadSkills(storeAgent, req.Skills),
            MaxIterations = req.MaxIterations,
            Metadata = BuildMetadata(req, resolvedProvider.ProviderId, modelOverride ?? resolvedProvider.DefaultModel),
        };

        var streamInterrupted = false;
        try
        {
            await foreach (var chunk in agent.RunAsync(run, ct).ConfigureAwait(false))
            {
                transcript.Apply(chunk);
                await WriteSse(SseEventName(chunk.Kind), SerializeChunk(chunk), ct);
            }
        }
        catch (OperationCanceledException)
        {
            streamInterrupted = true;
            _logger.LogInformation("chat/stream interrupted for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "chat/stream failed");
            transcript.Apply(StreamingChunk.Error(ex.Message));
            await WriteSse("error", new { message = ex.Message }, HttpContext.RequestAborted).ConfigureAwait(false);
        }

        state.Messages.AddRange(transcript.Complete());
        await _sessions.SaveAsync(state, streamInterrupted ? CancellationToken.None : ct).ConfigureAwait(false);

        if (!streamInterrupted)
            await WriteSse("done", new { sessionId }, ct).ConfigureAwait(false);
    }


    private static string? BuildSystemPromptOverride(Agent? storeAgent, string? expertPrompt)
    {
        var parts = new[] { storeAgent?.SystemPrompt, expertPrompt }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return parts.Length == 0 ? null : string.Join("\n\n", parts);
    }

    private static string[]? BuildPreloadSkills(Agent? storeAgent, string[]? requestSkills)
    {
        var skills = new List<string>();
        if (requestSkills is not null) skills.AddRange(requestSkills.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (storeAgent is not null)
        {
            skills.AddRange(storeAgent.Skills
                .Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.SkillId))
                .OrderByDescending(s => s.Priority)
                .Select(s => s.SkillId));
        }

        var distinct = skills.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return distinct.Length == 0 ? null : distinct;
    }


    private sealed class StreamTranscriptBuilder
    {
        private readonly List<ChatMessage> _messages = [];
        private readonly System.Text.StringBuilder _assistantText = new();
        private readonly Dictionary<string, PendingToolMessage> _tools = new(StringComparer.Ordinal);

        public void Apply(StreamingChunk chunk)
        {
            switch (chunk.Kind)
            {
                case StreamingChunkKind.TextDelta:
                    if (!string.IsNullOrEmpty(chunk.Text)) _assistantText.Append(chunk.Text);
                    break;
                case StreamingChunkKind.ThinkingDelta:
                    if (!string.IsNullOrEmpty(chunk.Text))
                        _messages.Add(Message(ChatRole.Assistant, [ContentPart.FromText(chunk.Text)], chunk, "thinking"));
                    break;
                case StreamingChunkKind.ToolCallStart:
                    FlushAssistant(chunk);
                    if (!string.IsNullOrWhiteSpace(chunk.ToolCallId))
                    {
                        _tools[chunk.ToolCallId] = new PendingToolMessage(chunk.ToolCallId, chunk.ToolName ?? string.Empty, chunk.ToolArgsJson ?? string.Empty, chunk);
                    }
                    break;
                case StreamingChunkKind.ToolCallArgsDelta:
                    if (!string.IsNullOrWhiteSpace(chunk.ToolCallId) && _tools.TryGetValue(chunk.ToolCallId, out var pendingArgs))
                        pendingArgs.Args.Append(chunk.ToolArgsJson ?? string.Empty);
                    break;
                case StreamingChunkKind.ToolCallEnd:
                    if (!string.IsNullOrWhiteSpace(chunk.ToolCallId) && _tools.TryGetValue(chunk.ToolCallId, out var pending))
                    {
                        if (!string.IsNullOrWhiteSpace(chunk.ToolArgsJson))
                        {
                            pending.Args.Clear();
                            pending.Args.Append(chunk.ToolArgsJson);
                        }
                        var args = pending.Args.ToString().Trim();
                        _messages.Add(Message(ChatRole.Assistant, [ContentPart.ToolCall(pending.Id, pending.Name, string.IsNullOrWhiteSpace(args) ? "{}" : args)], chunk, "tool"));
                        _tools.Remove(chunk.ToolCallId);
                    }
                    break;
                case StreamingChunkKind.ToolObservation:
                    _messages.Add(Message(ChatRole.Tool, [ContentPart.ToolResult(chunk.ToolCallId ?? Guid.NewGuid().ToString("N"), chunk.Text ?? string.Empty, chunk.Text)], chunk, "observation"));
                    break;
                case StreamingChunkKind.Warning:
                    if (!string.IsNullOrEmpty(chunk.Text))
                        _messages.Add(Message(ChatRole.Assistant, [ContentPart.FromText(chunk.Text)], chunk, "warning"));
                    break;
                case StreamingChunkKind.Error:
                    if (!string.IsNullOrEmpty(chunk.Text))
                        _messages.Add(Message(ChatRole.Assistant, [ContentPart.FromText(chunk.Text)], chunk, "error"));
                    break;
                case StreamingChunkKind.End:
                    FlushAssistant(chunk);
                    break;
            }
        }

        public IReadOnlyList<ChatMessage> Complete()
        {
            FlushAssistant(null);
            foreach (var pending in _tools.Values)
            {
                var args = pending.Args.ToString().Trim();
                _messages.Add(Message(ChatRole.Assistant, [ContentPart.ToolCall(pending.Id, pending.Name, string.IsNullOrWhiteSpace(args) ? "{}" : args)], pending.Source, "tool"));
            }
            _tools.Clear();
            return _messages;
        }

        private void FlushAssistant(StreamingChunk? chunk)
        {
            if (_assistantText.Length == 0) return;
            _messages.Add(Message(ChatRole.Assistant, [ContentPart.FromText(_assistantText.ToString())], chunk, "assistant"));
            _assistantText.Clear();
        }

        private static ChatMessage Message(ChatRole role, IReadOnlyList<ContentPart> parts, StreamingChunk? chunk, string displayKind)
        {
            var meta = new Dictionary<string, string> { ["displayKind"] = displayKind };
            if (!string.IsNullOrWhiteSpace(chunk?.AgentId)) meta["agentId"] = chunk.AgentId!;
            if (!string.IsNullOrWhiteSpace(chunk?.SkillId)) meta["skillId"] = chunk.SkillId!;
            if (!string.IsNullOrWhiteSpace(chunk?.ToolName)) meta["toolName"] = chunk.ToolName!;
            if (chunk?.Step is int step) meta["step"] = step.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new ChatMessage { Role = role, Parts = parts, Meta = meta };
        }

        private sealed class PendingToolMessage
        {
            public PendingToolMessage(string id, string name, string args, StreamingChunk source)
            {
                Id = id;
                Name = name;
                Args = new System.Text.StringBuilder(args);
                Source = source;
            }

            public string Id { get; }
            public string Name { get; }
            public System.Text.StringBuilder Args { get; }
            public StreamingChunk Source { get; }
        }
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
    public bool ReuseLastUserMessage { get; set; }
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
