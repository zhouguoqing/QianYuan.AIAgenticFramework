using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Exceptions;
using QianYuan.Core.Models;
using QianYuan.Core.Streaming;

namespace QianYuan.Providers.OpenAICompat;

/// <summary>
/// OpenAI Chat Completions compatible provider.
/// Supports streaming SSE, multi-part user content (vision), tool calls (including parallel and streamed args).
/// </summary>
public sealed class OpenAICompatProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly OpenAICompatOptions _opts;
    private readonly ILogger<OpenAICompatProvider> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public OpenAICompatProvider(HttpClient http, OpenAICompatOptions opts, ILogger<OpenAICompatProvider> logger)
    {
        _http = http;
        _opts = opts;
        _logger = logger;
        ConfigureHttp();
    }

    public string ProviderId => _opts.ProviderId;
    public string DefaultModel => _opts.DefaultModel;

    public LlmCapabilities Capabilities
    {
        get
        {
            var caps = LlmCapabilities.Streaming | LlmCapabilities.JsonMode;
            if (_opts.SupportsTools) caps |= LlmCapabilities.Tools;
            if (_opts.SupportsParallelToolCalls) caps |= LlmCapabilities.ParallelToolCalls;
            if (_opts.SupportsVision) caps |= LlmCapabilities.Vision;
            return caps;
        }
    }

    private void ConfigureHttp()
    {
        _http.Timeout = _opts.Timeout;
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(_opts.BaseUrl.TrimEnd('/') + "/");
        if (_http.DefaultRequestHeaders.Authorization is null)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);
        if (!string.IsNullOrEmpty(_opts.Organization) && !_http.DefaultRequestHeaders.Contains("OpenAI-Organization"))
            _http.DefaultRequestHeaders.Add("OpenAI-Organization", _opts.Organization);
    }

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        var body = BuildBody(request, stream: false);
        using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContentFromObject(body)
        };
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadFromJsonAsync<JsonNode>(JsonOpts, ct).ConfigureAwait(false)
                   ?? throw new LlmProviderException(ProviderId, "empty response");

        var choices = json["choices"]?.AsArray();
        var choice = choices is { Count: > 0 } ? choices[0] : null;
        var msgNode = choice?["message"];
        var parts = new List<ContentPart>();
        var text = msgNode?["content"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(text)) parts.Add(ContentPart.FromText(text));

        var toolCalls = msgNode?["tool_calls"]?.AsArray();
        if (toolCalls is not null)
        {
            foreach (var tc in toolCalls)
            {
                var id = tc?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
                var name = tc?["function"]?["name"]?.GetValue<string>() ?? "";
                var args = tc?["function"]?["arguments"]?.GetValue<string>() ?? "{}";
                parts.Add(ContentPart.ToolCall(id, name, args));
            }
        }

        var usage = json["usage"];
        return new ChatResponse
        {
            Message = ChatMessage.Assistant(parts),
            FinishReason = choice?["finish_reason"]?.GetValue<string>(),
            Model = json["model"]?.GetValue<string>(),
            Usage = usage is null ? null : new TokenUsage(
                usage["prompt_tokens"]?.GetValue<int>() ?? 0,
                usage["completion_tokens"]?.GetValue<int>() ?? 0)
        };
    }

    public async IAsyncEnumerable<StreamingChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildBody(request, stream: true);
        using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContentFromObject(body)
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp, ct).ConfigureAwait(false);

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        // Track per-index tool call state because OpenAI streams args delta-by-delta.
        var toolByIndex = new Dictionary<int, (string id, string name)>();
        string? model = null;
        string? finishReason = null;
        TokenUsage? usage = null;
        var startedEmitted = false;

        await foreach (var payload in SseReader.ReadAsync(stream, ct))
        {
            if (payload == "[DONE]") break;

            JsonNode? node;
            try { node = JsonNode.Parse(payload); }
            catch { continue; }
            if (node is null) continue;

            model ??= node["model"]?.GetValue<string>();
            if (!startedEmitted)
            {
                yield return StreamingChunk.Start(model);
                startedEmitted = true;
            }

            var choices = node["choices"]?.AsArray();
            var choice = choices is { Count: > 0 } ? choices[0] : null;
            var delta = choice?["delta"];
            var content = delta?["content"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(content))
                yield return StreamingChunk.OfText(content);

            var calls = delta?["tool_calls"]?.AsArray();
            if (calls is not null)
            {
                foreach (var c in calls)
                {
                    var index = c?["index"]?.GetValue<int>() ?? 0;
                    if (!toolByIndex.TryGetValue(index, out var existing))
                    {
                        var id = c?["id"]?.GetValue<string>() ?? $"call_{index}_{Guid.NewGuid():N}";
                        var name = c?["function"]?["name"]?.GetValue<string>() ?? "";
                        toolByIndex[index] = (id, name);
                        yield return new StreamingChunk
                        {
                            Kind = StreamingChunkKind.ToolCallStart,
                            ToolCallId = id,
                            ToolName = name,
                            ToolArgsJson = c?["function"]?["arguments"]?.GetValue<string>(),
                        };
                    }
                    else
                    {
                        var argsDelta = c?["function"]?["arguments"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(argsDelta))
                        {
                            yield return new StreamingChunk
                            {
                                Kind = StreamingChunkKind.ToolCallArgsDelta,
                                ToolCallId = existing.id,
                                ToolName = existing.name,
                                ToolArgsJson = argsDelta,
                            };
                        }
                    }
                }
            }

            var fr = choice?["finish_reason"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(fr))
            {
                finishReason = fr;
                foreach (var (_, info) in toolByIndex)
                {
                    yield return new StreamingChunk
                    {
                        Kind = StreamingChunkKind.ToolCallEnd,
                        ToolCallId = info.id,
                        ToolName = info.name,
                    };
                }
            }

            var u = node["usage"];
            if (u is not null)
            {
                usage = new TokenUsage(
                    u["prompt_tokens"]?.GetValue<int>() ?? 0,
                    u["completion_tokens"]?.GetValue<int>() ?? 0);
                yield return new StreamingChunk { Kind = StreamingChunkKind.Usage, Usage = usage };
            }
        }

        yield return StreamingChunk.End(finishReason ?? "stop", usage);
    }

    /// <summary>
    /// Serialise the body to a fixed-length byte array so the request always carries
    /// an explicit Content-Length header and never falls back to Transfer-Encoding:
    /// chunked. Some OpenAI-compatible gateways (new-api / one-api) reject chunked
    /// request bodies with "invalid byte in chunk length".
    /// </summary>
    private static HttpContent JsonContentFromObject(object body)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, JsonOpts);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private object BuildBody(ChatRequest request, bool stream)
    {
        var messages = new List<object>();
        foreach (var m in request.Messages)
            messages.Add(SerializeMessage(m));

        var body = new Dictionary<string, object?>
        {
            ["model"] = request.Options.Model ?? _opts.DefaultModel,
            ["messages"] = messages,
            ["stream"] = stream,
        };
        if (_opts.SupportsSamplingParams)
        {
            if (request.Options.Temperature is not null) body["temperature"] = request.Options.Temperature;
            if (request.Options.TopP is not null) body["top_p"] = request.Options.TopP;
            if (request.Options.MaxOutputTokens is not null) body["max_tokens"] = request.Options.MaxOutputTokens;
            if (request.Options.StopSequences is { Count: > 0 }) body["stop"] = request.Options.StopSequences;
        }

        if (request.Tools is { Count: > 0 } && _opts.SupportsTools)
        {
            var tools = request.Tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = JsonNode.Parse(t.JsonSchema)
                }
            }).Cast<object>().ToArray();
            body["tools"] = tools;
            body["tool_choice"] = request.Options.ToolChoice ?? "auto";
            if (_opts.SupportsParallelToolCalls) body["parallel_tool_calls"] = true;
        }

        if (stream) body["stream_options"] = new { include_usage = true };
        return body;
    }

    private static object SerializeMessage(ChatMessage m)
    {
        var role = m.Role switch
        {
            ChatRole.System => "system",
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "tool",
            _ => "user"
        };

        // Tool result: must use the "tool" role with a tool_call_id.
        if (m.Role == ChatRole.Tool)
        {
            var resultPart = m.Parts.FirstOrDefault(p => p.Kind == ContentKind.ToolResult);
            return new
            {
                role,
                tool_call_id = resultPart?.ToolCallId ?? "",
                content = resultPart?.JsonPayload ?? resultPart?.Text ?? ""
            };
        }

        // Assistant turn carrying tool calls.
        if (m.Role == ChatRole.Assistant && m.Parts.Any(p => p.Kind == ContentKind.ToolCall))
        {
            var textPart = m.Parts.FirstOrDefault(p => p.Kind == ContentKind.Text);
            var calls = m.Parts.Where(p => p.Kind == ContentKind.ToolCall).Select(p => new
            {
                id = p.ToolCallId,
                type = "function",
                function = new { name = p.Name, arguments = p.JsonPayload ?? "{}" }
            }).ToArray();
            return new
            {
                role,
                content = textPart?.Text ?? "",
                tool_calls = calls
            };
        }

        // Multimodal user content: split parts.
        if (m.Role == ChatRole.User && m.Parts.Any(p => p.Kind == ContentKind.Image))
        {
            var arr = new List<object>();
            foreach (var p in m.Parts)
            {
                if (p.Kind == ContentKind.Text && p.Text is not null)
                    arr.Add(new { type = "text", text = p.Text });
                else if (p.Kind == ContentKind.Image && p.DataUrlOrBase64 is not null)
                {
                    var url = p.DataUrlOrBase64.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                              || p.DataUrlOrBase64.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                        ? p.DataUrlOrBase64
                        : $"data:{p.MimeType ?? "image/png"};base64,{p.DataUrlOrBase64}";
                    arr.Add(new { type = "image_url", image_url = new { url } });
                }
            }
            return new { role, content = arr };
        }

        return new { role, content = m.AsPlainText() };
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        string body;
        try { body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
        catch { body = ""; }
        throw new LlmProviderException(ProviderId, $"HTTP {(int)resp.StatusCode}: {body}", (int)resp.StatusCode);
    }
}
