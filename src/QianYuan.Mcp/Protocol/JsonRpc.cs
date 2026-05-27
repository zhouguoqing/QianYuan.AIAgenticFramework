using System.Text.Json.Nodes;

namespace QianYuan.Mcp.Protocol;

/// <summary>JSON-RPC 2.0 message helpers used by both MCP client and server.</summary>
public static class JsonRpc
{
    public const string Version = "2.0";

    public static JsonObject Request(object id, string method, JsonNode? @params = null)
    {
        var o = new JsonObject
        {
            ["jsonrpc"] = Version,
            ["id"] = JsonValue.Create(id),
            ["method"] = method,
        };
        if (@params is not null) o["params"] = @params;
        return o;
    }

    public static JsonObject Notification(string method, JsonNode? @params = null)
    {
        var o = new JsonObject { ["jsonrpc"] = Version, ["method"] = method };
        if (@params is not null) o["params"] = @params;
        return o;
    }

    public static JsonObject Result(object id, JsonNode? result)
    {
        return new JsonObject
        {
            ["jsonrpc"] = Version,
            ["id"] = JsonValue.Create(id),
            ["result"] = result ?? new JsonObject(),
        };
    }

    public static JsonObject Error(object? id, int code, string message, JsonNode? data = null)
    {
        var err = new JsonObject { ["code"] = code, ["message"] = message };
        if (data is not null) err["data"] = data;
        return new JsonObject
        {
            ["jsonrpc"] = Version,
            ["id"] = id is null ? null : JsonValue.Create(id),
            ["error"] = err,
        };
    }

    // Standard JSON-RPC error codes.
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
}

/// <summary>Well-known MCP method names.</summary>
public static class McpMethods
{
    public const string Initialize = "initialize";
    public const string Initialized = "notifications/initialized";
    public const string ToolsList = "tools/list";
    public const string ToolsCall = "tools/call";
    public const string ResourcesList = "resources/list";
    public const string ResourcesRead = "resources/read";
    public const string PromptsList = "prompts/list";
    public const string Ping = "ping";
}

public static class McpProtocolInfo
{
    public const string ProtocolVersion = "2024-11-05";
}
