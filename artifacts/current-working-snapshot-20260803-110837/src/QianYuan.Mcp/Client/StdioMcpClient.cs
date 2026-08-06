using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using QianYuan.Core.Abstractions;
using QianYuan.Mcp.Protocol;

namespace QianYuan.Mcp.Client;

/// <summary>
/// MCP client over stdio: launches an MCP server process and speaks newline-delimited JSON-RPC 2.0
/// on its stdin/stdout. This is the most common MCP transport (npx-based servers, python servers, etc.).
/// </summary>
public sealed class StdioMcpClient : IMcpClient
{
    private readonly McpStdioServerConfig _config;
    private readonly ILogger _logger;
    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _readLoop;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonNode?>> _pending = new();
    private long _nextId;
    private readonly CancellationTokenSource _cts = new();

    public StdioMcpClient(McpStdioServerConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public string ServerId => _config.ServerId;
    public bool IsConnected => _process is { HasExited: false };

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _config.Command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8,
        };
        foreach (var a in _config.Arguments) psi.ArgumentList.Add(a);
        foreach (var (k, v) in _config.Environment) psi.Environment[k] = v;

        _process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start MCP server '{_config.ServerId}'.");
        _stdin = _process.StandardInput;
        _readLoop = Task.Run(() => ReadLoopAsync(_process.StandardOutput, _cts.Token));
        _ = Task.Run(() => DrainStderrAsync(_process.StandardError, _cts.Token));

        // initialize handshake
        var initParams = new JsonObject
        {
            ["protocolVersion"] = McpProtocolInfo.ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "QianYuan", ["version"] = "0.1.0" }
        };
        await SendRequestAsync(McpMethods.Initialize, initParams, ct).ConfigureAwait(false);
        await SendNotificationAsync(McpMethods.Initialized, null, ct).ConfigureAwait(false);
        _logger.LogInformation("MCP server {Server} connected (stdio).", _config.ServerId);
    }

    public async Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct = default)
    {
        var result = await SendRequestAsync(McpMethods.ToolsList, new JsonObject(), ct).ConfigureAwait(false);
        var tools = new List<McpTool>();
        var arr = result?["tools"]?.AsArray();
        if (arr is not null)
            foreach (var t in arr)
                tools.Add(new McpTool(
                    t?["name"]?.GetValue<string>() ?? "",
                    t?["description"]?.GetValue<string>() ?? "",
                    t?["inputSchema"]?.ToJsonString() ?? "{\"type\":\"object\"}"));
        return tools;
    }

    public async Task<McpToolResult> CallToolAsync(string toolName, string argumentsJson, CancellationToken ct = default)
    {
        JsonNode args; try { args = JsonNode.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson)!; }
        catch { args = new JsonObject(); }

        var p = new JsonObject { ["name"] = toolName, ["arguments"] = args };
        var result = await SendRequestAsync(McpMethods.ToolsCall, p, ct).ConfigureAwait(false);

        var isError = result?["isError"]?.GetValue<bool>() ?? false;
        var sb = new StringBuilder();
        var content = result?["content"]?.AsArray();
        if (content is not null)
            foreach (var item in content)
                if (item?["type"]?.GetValue<string>() == "text")
                    sb.Append(item["text"]?.GetValue<string>());

        var text = sb.ToString();
        return new McpToolResult(result?.ToJsonString() ?? "{}", isError, text);
    }

    public async Task<IReadOnlyList<McpResource>> ListResourcesAsync(CancellationToken ct = default)
    {
        var result = await SendRequestAsync(McpMethods.ResourcesList, new JsonObject(), ct).ConfigureAwait(false);
        var list = new List<McpResource>();
        var arr = result?["resources"]?.AsArray();
        if (arr is not null)
            foreach (var r in arr)
                list.Add(new McpResource(
                    r?["uri"]?.GetValue<string>() ?? "",
                    r?["name"]?.GetValue<string>() ?? "",
                    r?["mimeType"]?.GetValue<string>()));
        return list;
    }

    public async Task<string> ReadResourceAsync(string uri, CancellationToken ct = default)
    {
        var p = new JsonObject { ["uri"] = uri };
        var result = await SendRequestAsync(McpMethods.ResourcesRead, p, ct).ConfigureAwait(false);
        var sb = new StringBuilder();
        var contents = result?["contents"]?.AsArray();
        if (contents is not null)
            foreach (var c in contents)
                sb.Append(c?["text"]?.GetValue<string>());
        return sb.ToString();
    }

    private async Task<JsonNode?> SendRequestAsync(string method, JsonNode? @params, CancellationToken ct)
    {
        if (_stdin is null) throw new InvalidOperationException("MCP client not connected.");
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var msg = JsonRpc.Request(id, method, @params);
        await WriteMessageAsync(msg, ct).ConfigureAwait(false);

        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        using var timeout = new CancellationTokenSource(_config.RequestTimeout);
        using var reg2 = timeout.Token.Register(() => tcs.TrySetException(new TimeoutException($"MCP request '{method}' timed out.")));
        return await tcs.Task.ConfigureAwait(false);
    }

    private async Task SendNotificationAsync(string method, JsonNode? @params, CancellationToken ct)
    {
        await WriteMessageAsync(JsonRpc.Notification(method, @params), ct).ConfigureAwait(false);
    }

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private async Task WriteMessageAsync(JsonObject msg, CancellationToken ct)
    {
        var line = msg.ToJsonString();
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _stdin!.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
            await _stdin.FlushAsync(ct).ConfigureAwait(false);
        }
        finally { _writeLock.Release(); }
    }

    private async Task ReadLoopAsync(StreamReader stdout, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await stdout.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;
                if (line.Length == 0) continue;

                JsonNode? node; try { node = JsonNode.Parse(line); } catch { continue; }
                if (node is null) continue;

                var idNode = node["id"];
                if (idNode is not null && long.TryParse(idNode.ToString(), out var id) && _pending.TryRemove(id, out var tcs))
                {
                    var err = node["error"];
                    if (err is not null)
                        tcs.TrySetException(new InvalidOperationException($"MCP error {err["code"]}: {err["message"]}"));
                    else
                        tcs.TrySetResult(node["result"]);
                }
                // server-initiated requests/notifications are ignored in this minimal client.
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogError(ex, "MCP read loop failed for {Server}", _config.ServerId); }
    }

    private async Task DrainStderrAsync(StreamReader stderr, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await stderr.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;
                if (line.Length > 0) _logger.LogDebug("[mcp:{Server}] {Line}", _config.ServerId, line);
            }
        }
        catch { /* ignore */ }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        try { _stdin?.Dispose(); } catch { }
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch { }
        _process?.Dispose();
        _cts.Dispose();
    }
}

public sealed class McpStdioServerConfig
{
    public required string ServerId { get; init; }
    public required string Command { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(60);
}
