using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Memory;
using QianYuan.Core.Models;
using QianYuan.Core.Streaming;

namespace QianYuan.Api.Hubs;

/// <summary>
/// SignalR hub mirroring the SSE chat endpoint. Useful for browser apps that prefer bidirectional
/// real-time channels (cancellation, server-initiated push) over plain SSE.
///
/// Client API: invoke("Send", request) -> server streams chunks back via "OnChunk" events,
/// then a final "OnDone" event.
/// </summary>
public sealed class ChatHub : Hub
{
    private readonly IAgentRegistry _agents;
    private readonly ISessionStore _sessions;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(IAgentRegistry agents, ISessionStore sessions, ILogger<ChatHub> logger)
    {
        _agents = agents; _sessions = sessions; _logger = logger;
    }

    public async IAsyncEnumerable<object> Send(
        Controllers.ChatStreamRequest req,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var agent = _agents.Get(req.AgentId ?? "qianyuan.default") ?? _agents.List().FirstOrDefault();
        if (agent is null)
        {
            yield return new { kind = "Error", text = "no agents" };
            yield break;
        }

        var sessionId = req.SessionId ?? Guid.NewGuid().ToString("N");
        var state = await _sessions.GetAsync(sessionId, ct).ConfigureAwait(false)
            ?? new SessionState { SessionId = sessionId, AgentId = agent.Id, OwnerId = req.OwnerId };

        var parts = new List<ContentPart>();
        if (!string.IsNullOrEmpty(req.UserText)) parts.Add(ContentPart.FromText(req.UserText));
        if (req.Images is not null)
            foreach (var img in req.Images)
                if (!string.IsNullOrEmpty(img.Url)) parts.Add(ContentPart.FromImageUrl(img.Url, img.Mime));
                else if (!string.IsNullOrEmpty(img.Base64)) parts.Add(ContentPart.FromImageBase64(img.Base64, img.Mime ?? "image/png"));
        if (parts.Count == 0) parts.Add(ContentPart.FromText(string.Empty));

        var messages = state.Messages.ToList();
        messages.Add(new ChatMessage { Role = ChatRole.User, Parts = parts });
        state.Messages.Clear();
        state.Messages.AddRange(messages);
        state.Title ??= req.UserText is { Length: > 0 } ? (req.UserText.Length <= 40 ? req.UserText : req.UserText[..40] + "...") : null;
        state.AgentId = agent.Id;

        var transcript = new HubTranscriptBuilder();

        var run = new AgentRunRequest
        {
            Messages = messages,
            SessionId = sessionId,
            ModelOverride = req.Model,
            ProviderOverride = req.Provider,
            PreloadSkills = req.Skills,
            MaxIterations = req.MaxIterations,
        };

        await foreach (var chunk in agent.RunAsync(run, ct).ConfigureAwait(false))
        {
            transcript.Apply(chunk);

            yield return new
            {
                kind = chunk.Kind.ToString(),
                text = chunk.Text,
                toolCallId = chunk.ToolCallId,
                toolName = chunk.ToolName,
                toolArgsJson = chunk.ToolArgsJson,
                finishReason = chunk.FinishReason,
                model = chunk.Model,
                step = chunk.Step,
            };
        }

        state.Messages.AddRange(transcript.Complete());
        await _sessions.SaveAsync(state, ct).ConfigureAwait(false);
    }

    private sealed class HubTranscriptBuilder
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
                        _tools[chunk.ToolCallId] = new PendingToolMessage(chunk.ToolCallId, chunk.ToolName ?? string.Empty, chunk.ToolArgsJson ?? string.Empty, chunk);
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

        private static ChatMessage Message(ChatRole role, IReadOnlyList<ContentPart> parts, StreamingChunk? chunk, string displayKind) => new()
        {
            Role = role,
            Parts = parts,
            Meta = chunk is null ? new Dictionary<string, string> { ["displayKind"] = displayKind } : BuildMeta(chunk, displayKind),
        };

        private sealed record PendingToolMessage(string Id, string Name, string InitialArgs, StreamingChunk Source)
        {
            public System.Text.StringBuilder Args { get; } = new(InitialArgs);
        }
    }

    private static IReadOnlyDictionary<string, string> BuildMeta(StreamingChunk chunk, string displayKind)
    {
        var meta = new Dictionary<string, string> { ["displayKind"] = displayKind };
        if (!string.IsNullOrWhiteSpace(chunk.AgentId)) meta["agentId"] = chunk.AgentId!;
        if (!string.IsNullOrWhiteSpace(chunk.SkillId)) meta["skillId"] = chunk.SkillId!;
        if (!string.IsNullOrWhiteSpace(chunk.ToolName)) meta["toolName"] = chunk.ToolName!;
        if (chunk.Step is int step) meta["step"] = step.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return meta;
    }
}
