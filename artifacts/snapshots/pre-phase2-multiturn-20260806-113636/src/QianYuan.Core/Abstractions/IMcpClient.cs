namespace QianYuan.Core.Abstractions;

/// <summary>
/// MCP client - connects to an external Model Context Protocol server (over stdio or HTTP/SSE)
/// and surfaces its tools so they can be mounted into a skill catalog.
/// </summary>
public interface IMcpClient : IAsyncDisposable
{
    string ServerId { get; }
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken ct = default);

    Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct = default);

    Task<McpToolResult> CallToolAsync(string toolName, string argumentsJson, CancellationToken ct = default);

    Task<IReadOnlyList<McpResource>> ListResourcesAsync(CancellationToken ct = default);

    Task<string> ReadResourceAsync(string uri, CancellationToken ct = default);
}

public sealed record McpTool(string Name, string Description, string JsonSchema);
public sealed record McpResource(string Uri, string Name, string? MimeType);
public sealed record McpToolResult(string JsonContent, bool IsError, string? Text = null);
