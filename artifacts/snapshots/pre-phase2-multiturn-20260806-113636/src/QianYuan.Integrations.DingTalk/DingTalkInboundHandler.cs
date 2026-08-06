using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;
using QianYuan.Core.Streaming;

namespace QianYuan.Integrations.DingTalk;

/// <summary>
/// Inbound DingTalk message handler. Parses the JSON body posted by a DingTalk bot, verifies the
/// HMAC signature, runs the configured agent, and returns a markdown reply.
///
/// Streaming over DingTalk is approximated by chunking the agent's stream into batches and
/// updating the reply via the outgoing webhook (chat-bot custom robot). For real-time updates,
/// hosts can replace this with the DingTalk Stream API.
/// </summary>
public sealed class DingTalkInboundHandler
{
    private readonly IAgentRegistry _agents;
    private readonly DingTalkOutgoingClient _out;
    private readonly DingTalkOptions _opts;
    private readonly ILogger<DingTalkInboundHandler> _logger;

    public DingTalkInboundHandler(
        IAgentRegistry agents,
        DingTalkOutgoingClient outClient,
        IOptions<DingTalkOptions> opts,
        ILogger<DingTalkInboundHandler> logger)
    {
        _agents = agents; _out = outClient; _opts = opts.Value; _logger = logger;
    }

    /// <summary>Verify DingTalk callback signature. timestamp + body hashed with appSecret.</summary>
    public bool VerifySignature(string timestamp, string sign, string body)
    {
        if (string.IsNullOrEmpty(_opts.AppSecret)) return true; // signing not enforced

        var stringToSign = $"{timestamp}\n{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_opts.AppSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        var expected = Convert.ToBase64String(hash);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(sign));
    }

    /// <summary>
    /// Handle a parsed DingTalk message payload. Streams the agent's output, periodically updating
    /// the markdown reply via <see cref="DingTalkOutgoingClient"/>.
    /// </summary>
    public async Task<DingTalkReply> HandleAsync(JsonNode payload, CancellationToken ct = default)
    {
        var text = payload["text"]?["content"]?.GetValue<string>()?.Trim() ?? "";
        var senderId = payload["senderStaffId"]?.GetValue<string>() ?? payload["senderId"]?.GetValue<string>() ?? "anon";
        var conversationId = payload["conversationId"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");

        var agent = _agents.Get(_opts.DefaultAgentId)
                    ?? _agents.List().FirstOrDefault()
                    ?? throw new InvalidOperationException("No agent registered for DingTalk.");

        var run = new AgentRunRequest
        {
            Messages = new[] { ChatMessage.User(text) },
            SessionId = $"dingtalk:{senderId}:{conversationId}",
            Metadata = new Dictionary<string, string> { ["channel"] = "dingtalk", ["sender"] = senderId },
        };

        var sb = new StringBuilder();
        var lastFlush = DateTimeOffset.UtcNow;

        await foreach (var chunk in agent.RunAsync(run, ct).ConfigureAwait(false))
        {
            if (chunk.Kind == StreamingChunkKind.TextDelta && chunk.Text is { Length: > 0 })
            {
                sb.Append(chunk.Text);
                // Push partial update every ~1.5s to simulate streaming.
                if ((DateTimeOffset.UtcNow - lastFlush).TotalMilliseconds > 1500)
                {
                    lastFlush = DateTimeOffset.UtcNow;
                    try { await _out.SendMarkdownAsync("QianYuan", sb.ToString() + " ▍", ct: ct).ConfigureAwait(false); }
                    catch (Exception ex) { _logger.LogWarning(ex, "interim send failed"); }
                }
            }
            else if (chunk.Kind == StreamingChunkKind.Error)
            {
                sb.AppendLine($"\n\n> 错误: {chunk.Text}");
            }
        }

        var final = sb.ToString();
        try { await _out.SendMarkdownAsync("QianYuan", final, ct: ct).ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogError(ex, "final send failed"); }

        return new DingTalkReply(final);
    }
}

public sealed record DingTalkReply(string Markdown);
