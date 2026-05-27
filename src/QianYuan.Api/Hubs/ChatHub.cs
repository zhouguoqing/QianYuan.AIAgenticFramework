using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using QianYuan.Core.Abstractions;
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
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(IAgentRegistry agents, ILogger<ChatHub> logger)
    {
        _agents = agents; _logger = logger;
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

        var parts = new List<ContentPart>();
        if (!string.IsNullOrEmpty(req.UserText)) parts.Add(ContentPart.FromText(req.UserText));
        if (req.Images is not null)
            foreach (var img in req.Images)
                if (!string.IsNullOrEmpty(img.Url)) parts.Add(ContentPart.FromImageUrl(img.Url, img.Mime));
                else if (!string.IsNullOrEmpty(img.Base64)) parts.Add(ContentPart.FromImageBase64(img.Base64, img.Mime ?? "image/png"));

        var run = new AgentRunRequest
        {
            Messages = new[] { new ChatMessage { Role = ChatRole.User, Parts = parts } },
            SessionId = req.SessionId ?? Guid.NewGuid().ToString("N"),
            ModelOverride = req.Model,
            ProviderOverride = req.Provider,
            PreloadSkills = req.Skills,
            MaxIterations = req.MaxIterations,
        };

        await foreach (var chunk in agent.RunAsync(run, ct).ConfigureAwait(false))
        {
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
    }
}
