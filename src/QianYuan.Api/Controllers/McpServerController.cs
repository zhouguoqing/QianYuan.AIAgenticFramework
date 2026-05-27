using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using QianYuan.Mcp.Server;

namespace QianYuan.Api.Controllers;

/// <summary>
/// HTTP transport for the QianYuan MCP server. JSON-RPC 2.0 over POST. Optionally streams
/// notifications via the dedicated SSE endpoint.
/// </summary>
[ApiController]
[Route("api/mcp")]
public sealed class McpServerController : ControllerBase
{
    private readonly McpServerCore _core;
    public McpServerController(McpServerCore core) => _core = core;

    [HttpPost]
    public async Task<IActionResult> Rpc([FromBody] JsonObject request, CancellationToken ct)
    {
        var response = await _core.HandleAsync(request, ct);
        return response is null ? Ok(new JsonObject()) : Ok(response);
    }

    /// <summary>Minimal SSE channel used by streaming-aware MCP HTTP clients. Sends a ping every 25s.</summary>
    [HttpGet("events")]
    public async Task Events(CancellationToken ct)
    {
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache, no-transform";
        var ping = System.Text.Encoding.UTF8.GetBytes("event: ping\ndata: {}\n\n");
        while (!ct.IsCancellationRequested)
        {
            await Response.Body.WriteAsync(ping, ct);
            await Response.Body.FlushAsync(ct);
            try { await Task.Delay(TimeSpan.FromSeconds(25), ct); } catch { break; }
        }
    }
}
