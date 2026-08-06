using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using QianYuan.Api.Services;
using QianYuan.Mcp.Protocol;
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
    private readonly IWorkTaskExecutionHarness _harness;
    private readonly IWorkTaskService _workTasks;

    public McpServerController(McpServerCore core, IWorkTaskExecutionHarness harness, IWorkTaskService workTasks)
    {
        _core = core;
        _harness = harness;
        _workTasks = workTasks;
    }

    [HttpPost]
    public async Task<IActionResult> Rpc([FromBody] JsonObject request, CancellationToken ct)
    {
        var method = request["method"]?.GetValue<string>();
        if (method is McpMethods.TasksList or McpMethods.TasksGet or McpMethods.TasksResult or McpMethods.TasksCancel)
        {
            return Ok(await HandleTaskMethodAsync(request, method, ct));
        }
        var response = await _core.HandleAsync(request, ct);
        return response is null ? Ok(new JsonObject()) : Ok(response);
    }

    private async Task<JsonObject> HandleTaskMethodAsync(JsonObject request, string method, CancellationToken ct)
    {
        var idNode = request["id"];
        object id = idNode?.GetValueKind() == System.Text.Json.JsonValueKind.Number
            ? idNode.GetValue<long>()
            : (object)(idNode?.GetValue<string>() ?? "0");
        var @params = request["params"];

        var userId = ParseGuid(@params?["userId"]?.GetValue<string>());
        if (userId is null) return JsonRpc.Error(id, JsonRpc.InvalidParams, "missing params.userId");

        if (method == McpMethods.TasksList)
        {
            var runtimes = await _harness.ListRuntimesAsync(userId.Value, ct);
            var tasks = new JsonArray();
            foreach (var runtime in runtimes)
            {
                tasks.Add(new JsonObject
                {
                    ["taskId"] = runtime.TaskId.ToString(),
                    ["status"] = runtime.Status,
                    ["running"] = runtime.IsRunning,
                    ["startedAt"] = runtime.StartedAt,
                    ["finishedAt"] = runtime.FinishedAt,
                });
            }
            return JsonRpc.Result(id, new JsonObject { ["tasks"] = tasks });
        }

        var taskId = ParseGuid(@params?["taskId"]?.GetValue<string>());
        if (taskId is null) return JsonRpc.Error(id, JsonRpc.InvalidParams, "missing params.taskId");

        if (method == McpMethods.TasksGet)
        {
            try
            {
                var runtime = await _harness.GetRuntimeAsync(userId.Value, taskId.Value, ct);
                return JsonRpc.Result(id, new JsonObject
                {
                    ["taskId"] = runtime.TaskId.ToString(),
                    ["status"] = runtime.Status,
                    ["running"] = runtime.IsRunning,
                    ["startedAt"] = runtime.StartedAt,
                    ["finishedAt"] = runtime.FinishedAt,
                    ["lastError"] = runtime.LastError,
                });
            }
            catch (KeyNotFoundException)
            {
                return JsonRpc.Error(id, JsonRpc.InvalidParams, "task not found");
            }
        }

        if (method == McpMethods.TasksResult)
        {
            var detail = await _workTasks.GetAsync(userId.Value, taskId.Value, ct);
            if (detail is null) return JsonRpc.Error(id, JsonRpc.InvalidParams, "task not found");

            var artifacts = new JsonArray();
            foreach (var artifact in detail.Artifacts)
            {
                artifacts.Add(new JsonObject
                {
                    ["artifactId"] = artifact.Id.ToString(),
                    ["name"] = artifact.Name,
                    ["contentType"] = artifact.ContentType,
                    ["content"] = artifact.Content,
                    ["filePath"] = artifact.FilePath,
                    ["createdAt"] = artifact.CreatedAt,
                });
            }

            return JsonRpc.Result(id, new JsonObject
            {
                ["taskId"] = detail.Task.Id.ToString(),
                ["status"] = detail.Task.Status,
                ["artifacts"] = artifacts,
            });
        }

        try
        {
            var runtime = await _harness.CancelAsync(userId.Value, taskId.Value, @params?["reason"]?.GetValue<string>(), ct);
            return JsonRpc.Result(id, new JsonObject
            {
                ["taskId"] = runtime.TaskId.ToString(),
                ["status"] = runtime.Status,
                ["running"] = runtime.IsRunning,
                ["cancelReason"] = runtime.CancelReason,
            });
        }
        catch (KeyNotFoundException)
        {
            return JsonRpc.Error(id, JsonRpc.InvalidParams, "task not found");
        }
    }

    private static Guid? ParseGuid(string? value)
    {
        return Guid.TryParse(value, out var parsed) ? parsed : null;
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
