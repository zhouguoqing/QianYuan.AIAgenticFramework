using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using QianYuan.Core.Abstractions;
using QianYuan.Mcp.Protocol;

namespace QianYuan.Mcp.Server;

/// <summary>
/// MCP server core: dispatches JSON-RPC requests by exposing the kernel's skills as MCP tools.
/// Transport-agnostic — drive it from stdio (<see cref="StdioMcpServerHost"/>) or from an HTTP/SSE endpoint
/// in the WebAPI. Tools are named "&lt;skillId&gt;__&lt;toolName&gt;" (MCP tool names disallow dots in some clients).
/// </summary>
public sealed class McpServerCore
{
    private readonly ISkillManager _skills;
    private readonly IServiceProvider _services;
    private readonly ILogger _logger;
    private readonly string _serverName;

    public McpServerCore(ISkillManager skills, IServiceProvider services, ILogger logger, string serverName = "qianyuan")
    {
        _skills = skills;
        _services = services;
        _logger = logger;
        _serverName = serverName;
    }

    /// <summary>Handle a single JSON-RPC request object and return the response (or null for notifications).</summary>
    public async Task<JsonObject?> HandleAsync(JsonObject request, CancellationToken ct = default)
    {
        var method = request["method"]?.GetValue<string>();
        var idNode = request["id"];
        object? id = idNode is null ? null : (idNode.GetValueKind() == System.Text.Json.JsonValueKind.Number ? idNode.GetValue<long>() : idNode.GetValue<string>());

        if (method is null)
            return JsonRpc.Error(id, JsonRpc.InvalidRequest, "missing method");

        try
        {
            switch (method)
            {
                case McpMethods.Initialize:
                    return JsonRpc.Result(id!, new JsonObject
                    {
                        ["protocolVersion"] = McpProtocolInfo.ProtocolVersion,
                        ["capabilities"] = new JsonObject
                        {
                            ["tools"] = new JsonObject(),
                            ["tasks"] = new JsonObject
                            {
                                ["requests"] = new JsonObject
                                {
                                    ["tools/call"] = true,
                                }
                            }
                        },
                        ["serverInfo"] = new JsonObject { ["name"] = _serverName, ["version"] = "0.1.0" }
                    });

                case McpMethods.Initialized:
                case "notifications/cancelled":
                    return null; // notification

                case McpMethods.Ping:
                    return JsonRpc.Result(id!, new JsonObject());

                case McpMethods.ToolsList:
                    return JsonRpc.Result(id!, await BuildToolListAsync(ct).ConfigureAwait(false));

                case McpMethods.ToolsCall:
                    return await CallToolAsync(id!, request["params"], ct).ConfigureAwait(false);

                case McpMethods.ResourcesList:
                    return JsonRpc.Result(id!, new JsonObject { ["resources"] = new JsonArray() });

                default:
                    return JsonRpc.Error(id, JsonRpc.MethodNotFound, $"method not found: {method}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP server failed handling {Method}", method);
            return JsonRpc.Error(id, JsonRpc.InternalError, ex.Message);
        }
    }

    private async Task<JsonObject> BuildToolListAsync(CancellationToken ct)
    {
        var arr = new JsonArray();
        foreach (var manifest in _skills.ListManifests())
        {
            var skill = await _skills.GetAsync(manifest.Id, ct).ConfigureAwait(false);
            var tools = await skill.GetToolsAsync(ct).ConfigureAwait(false);
            foreach (var t in tools)
            {
                arr.Add(new JsonObject
                {
                    ["name"] = EncodeName(manifest.Id, t.Name),
                    ["description"] = t.Description,
                    ["inputSchema"] = JsonNode.Parse(t.JsonSchema),
                });
            }
        }
        return new JsonObject { ["tools"] = arr };
    }

    private async Task<JsonObject> CallToolAsync(object id, JsonNode? @params, CancellationToken ct)
    {
        var name = @params?["name"]?.GetValue<string>();
        if (name is null) return JsonRpc.Error(id, JsonRpc.InvalidParams, "missing tool name");

        var (skillId, toolName) = DecodeName(name);
        var args = @params?["arguments"]?.ToJsonString() ?? "{}";

        var skill = await _skills.GetAsync(skillId, ct).ConfigureAwait(false);
        var ctx = new SkillInvocationContext
        {
            AgentId = "mcp-server",
            SessionId = "mcp",
            Services = _services,
        };
        var result = await skill.InvokeAsync(toolName, args, ctx, ct).ConfigureAwait(false);

        return JsonRpc.Result(id, new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = result.HumanSummary ?? result.JsonContent }
            },
            ["isError"] = result.IsError,
        });
    }

    // MCP tool names: replace '.' with '__' for client compatibility, keeping a reversible scheme.
    private static string EncodeName(string skillId, string toolName) =>
        $"{skillId}__{toolName}".Replace('.', '_');

    private (string skillId, string toolName) DecodeName(string encoded)
    {
        // Match against the actual catalog so we recover the original (dotted) ids.
        foreach (var manifest in _skills.ListManifests())
        {
            var prefix = $"{manifest.Id}__".Replace('.', '_');
            if (encoded.StartsWith(prefix, StringComparison.Ordinal))
            {
                var tail = encoded[prefix.Length..];
                return (manifest.Id, tail);
            }
        }
        // Fall back: split on the last "__".
        var idx = encoded.LastIndexOf("__", StringComparison.Ordinal);
        return idx > 0 ? (encoded[..idx], encoded[(idx + 2)..]) : (encoded, encoded);
    }
}
