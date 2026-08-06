using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using QianYuan.Integrations.DingTalk;

namespace QianYuan.Api.Controllers;

[ApiController]
[Route("api/dingtalk")]
public sealed class DingTalkController : ControllerBase
{
    private readonly DingTalkInboundHandler _handler;
    private readonly ILogger<DingTalkController> _logger;

    public DingTalkController(DingTalkInboundHandler handler, ILogger<DingTalkController> logger)
    {
        _handler = handler; _logger = logger;
    }

    /// <summary>
    /// DingTalk custom-robot outgoing webhook entry. The bot platform posts a JSON body to this URL
    /// when a user @-mentions the bot; we verify the signature, run the agent, and either reply
    /// synchronously (200 + body) or asynchronously through the outgoing webhook.
    /// </summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook([FromHeader(Name = "timestamp")] string? timestamp,
                                             [FromHeader(Name = "sign")] string? sign,
                                             CancellationToken ct)
    {
        // Read the raw body so signature verification matches what DingTalk hashed.
        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync(ct);

        if (!string.IsNullOrEmpty(timestamp) && !string.IsNullOrEmpty(sign))
        {
            if (!_handler.VerifySignature(timestamp, sign, raw))
            {
                _logger.LogWarning("DingTalk signature mismatch");
                return Unauthorized();
            }
        }

        JsonNode? payload;
        try { payload = JsonNode.Parse(raw); } catch { return BadRequest("invalid JSON"); }
        if (payload is null) return BadRequest();

        // Fire-and-forget: DingTalk expects a fast 200; the handler will push markdown updates via outgoing webhook.
        _ = Task.Run(async () =>
        {
            try { await _handler.HandleAsync(payload, CancellationToken.None); }
            catch (Exception ex) { _logger.LogError(ex, "dingtalk handle failed"); }
        });

        return Ok(new { ok = true });
    }
}
