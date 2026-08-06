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

namespace QianYuan.Providers.QwenNative;

/// <summary>
/// Qwen (Tongyi Qianwen) provider speaking the native DashScope text-generation protocol:
///   POST /api/v1/services/aigc/text-generation/generation
///   POST /api/v1/services/aigc/multimodal-generation/generation
///
/// Streams via SSE when X-DashScope-SSE: enable. Tool calls follow OpenAI-ish shape inside DashScope response.
/// </summary>
public sealed class QwenProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly QwenOptions _opts;
    private readonly ILogger<QwenProvider> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public QwenProvider(HttpClient http, QwenOptions opts, ILogger<QwenProvider> logger)
    {
        _http = http; _opts = opts; _logger = logger;
        Configure();
    }

    public string ProviderId => _opts.ProviderId;
    public string DefaultModel => _opts.DefaultModel;

    public LlmCapabilities Capabilities =>
        LlmCapabilities.Streaming | LlmCapabilities.Tools | LlmCapabilities.Vision | LlmCapabilities.JsonMode;

    private void Configure()
    {
        _http.Timeout = _opts.Timeout;
        if (_http.BaseAddress is null) _http.BaseAddress = new Uri(_opts.BaseUrl.TrimEnd('/') + "/");
        if (_http.DefaultRequestHeaders.Authorization is null)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);
    }

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        var (path, body) = BuildRequest(request, streaming: false);
        using var resp = await _http.PostAsJsonAsync(path, body, JsonOpts, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadFromJsonAsync<JsonNode>(JsonOpts, ct).ConfigureAwait(false)!;

        var output = json["output"];
        var choice = output?["choices"]?[0];
        var msg = choice?["message"];
        var parts = new List<ContentPart>();

        var content = msg?["content"];
        if (content is JsonArray arr)
        {
            foreach (var item in arr)
            {
                var t = item?["text"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(t)) parts.Add(ContentPart.FromText(t));
            }
        }
        else
        {
            var text = content?.GetValue<string>();
            if (!string.IsNullOrEmpty(text)) parts.Add(ContentPart.FromText(text));
        }

        var toolCalls = msg?["tool_calls"]?.AsArray();
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
            Model = request.Options.Model ?? _opts.DefaultModel,
            Usage = usage is null ? null : new TokenUsage(
                usage["input_tokens"]?.GetValue<int>() ?? 0,
                usage["output_tokens"]?.GetValue<int>() ?? 0)
        };
    }

    public async IAsyncEnumerable<StreamingChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (path, body) = BuildRequest(request, streaming: true);
        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        { Content = JsonContent.Create(body, options: JsonOpts) };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.Headers.TryAddWithoutValidation("X-DashScope-SSE", "enable");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp, ct).ConfigureAwait(false);
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        var model = request.Options.Model ?? _opts.DefaultModel;
        yield return StreamingChunk.Start(model);

        string? finishReason = null;
        TokenUsage? usage = null;

        // For non-incremental, DashScope sends each event with the full accumulated text. We diff against the
        // previous emission so the upstream sees only the new bytes.
        var emittedSoFar = string.Empty;
        var toolByIndex = new Dictionary<int, (string id, string name)>();

        await foreach (var payload in SseReader.ReadAsync(stream, ct))
        {
            if (string.IsNullOrWhiteSpace(payload) || payload == "[DONE]") continue;
            JsonNode? node; try { node = JsonNode.Parse(payload); } catch { continue; }
            if (node is null) continue;

            var output = node["output"];
            var choice = output?["choices"]?[0];
            var msg = choice?["message"];
            var fr = choice?["finish_reason"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(fr) && fr != "null") finishReason = fr;

            // text
            string? text = null;
            var content = msg?["content"];
            if (content is JsonArray arr)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var it in arr)
                {
                    var t = it?["text"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(t)) sb.Append(t);
                }
                text = sb.ToString();
            }
            else
            {
                text = content?.GetValue<string>();
            }

            if (!string.IsNullOrEmpty(text))
            {
                string delta;
                if (_opts.IncrementalOutput)
                {
                    delta = text;
                }
                else
                {
                    delta = text.Length > emittedSoFar.Length && text.StartsWith(emittedSoFar, StringComparison.Ordinal)
                        ? text[emittedSoFar.Length..]
                        : text;
                    emittedSoFar = text;
                }
                if (delta.Length > 0) yield return StreamingChunk.OfText(delta);
            }

            var toolCalls = msg?["tool_calls"]?.AsArray();
            if (toolCalls is not null)
            {
                for (int i = 0; i < toolCalls.Count; i++)
                {
                    var tc = toolCalls[i];
                    var name = tc?["function"]?["name"]?.GetValue<string>() ?? "";
                    var args = tc?["function"]?["arguments"]?.GetValue<string>() ?? "{}";
                    var id = tc?["id"]?.GetValue<string>() ?? $"tc_{i}_{Guid.NewGuid():N}";
                    if (!toolByIndex.ContainsKey(i))
                    {
                        toolByIndex[i] = (id, name);
                        yield return new StreamingChunk
                        {
                            Kind = StreamingChunkKind.ToolCallStart,
                            ToolCallId = id, ToolName = name, ToolArgsJson = args
                        };
                    }
                    else
                    {
                        yield return new StreamingChunk
                        {
                            Kind = StreamingChunkKind.ToolCallArgsDelta,
                            ToolCallId = toolByIndex[i].id, ToolName = toolByIndex[i].name,
                            ToolArgsJson = args
                        };
                    }
                }
            }

            var u = node["usage"];
            if (u is not null)
            {
                usage = new TokenUsage(
                    u["input_tokens"]?.GetValue<int>() ?? 0,
                    u["output_tokens"]?.GetValue<int>() ?? 0);
            }
        }

        foreach (var (_, info) in toolByIndex)
            yield return new StreamingChunk
            {
                Kind = StreamingChunkKind.ToolCallEnd,
                ToolCallId = info.id, ToolName = info.name
            };

        yield return StreamingChunk.End(finishReason ?? "stop", usage);
    }

    private (string Path, object Body) BuildRequest(ChatRequest request, bool streaming)
    {
        var model = request.Options.Model ?? _opts.DefaultModel;
        var isMultimodal = _opts.AutoDetectMultimodal &&
                           (model.StartsWith("qwen-vl", StringComparison.OrdinalIgnoreCase) ||
                            model.StartsWith("qwen-omni", StringComparison.OrdinalIgnoreCase) ||
                            request.Messages.Any(m => m.Parts.Any(p => p.Kind == ContentKind.Image)));

        var messages = new List<object>();
        foreach (var m in request.Messages)
            messages.Add(SerializeMessage(m, isMultimodal));

        var parameters = new Dictionary<string, object?>
        {
            ["result_format"] = "message",
            ["incremental_output"] = streaming && _opts.IncrementalOutput,
        };
        if (request.Options.Temperature is not null) parameters["temperature"] = request.Options.Temperature;
        if (request.Options.TopP is not null) parameters["top_p"] = request.Options.TopP;
        if (request.Options.MaxOutputTokens is not null) parameters["max_tokens"] = request.Options.MaxOutputTokens;
        if (request.Options.StopSequences is { Count: > 0 }) parameters["stop"] = request.Options.StopSequences;
        if (request.Tools is { Count: > 0 })
        {
            parameters["tools"] = request.Tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = JsonNode.Parse(t.JsonSchema)
                }
            }).ToArray();
            if (request.Options.ToolChoice is { } tc)
                parameters["tool_choice"] = tc == "auto" ? "auto" : tc == "none" ? "none" :
                    (object)new { type = "function", function = new { name = tc } };
        }

        var body = new
        {
            model,
            input = new { messages },
            parameters
        };

        var path = isMultimodal
            ? "api/v1/services/aigc/multimodal-generation/generation"
            : "api/v1/services/aigc/text-generation/generation";
        return (path, body);
    }

    private static object SerializeMessage(ChatMessage m, bool multimodal)
    {
        var role = m.Role switch
        {
            ChatRole.System => "system",
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "tool",
            _ => "user"
        };

        if (m.Role == ChatRole.Tool)
        {
            var rp = m.Parts.FirstOrDefault(p => p.Kind == ContentKind.ToolResult);
            return new
            {
                role,
                tool_call_id = rp?.ToolCallId,
                content = rp?.JsonPayload ?? rp?.Text ?? ""
            };
        }

        if (m.Role == ChatRole.Assistant && m.Parts.Any(p => p.Kind == ContentKind.ToolCall))
        {
            var textPart = m.Parts.FirstOrDefault(p => p.Kind == ContentKind.Text);
            var calls = m.Parts.Where(p => p.Kind == ContentKind.ToolCall).Select(p => new
            {
                id = p.ToolCallId,
                type = "function",
                function = new { name = p.Name, arguments = p.JsonPayload ?? "{}" }
            }).ToArray();
            return new { role, content = textPart?.Text ?? "", tool_calls = calls };
        }

        if (multimodal)
        {
            var arr = new List<object>();
            foreach (var p in m.Parts)
            {
                if (p.Kind == ContentKind.Text && p.Text is not null) arr.Add(new { text = p.Text });
                else if (p.Kind == ContentKind.Image && p.DataUrlOrBase64 is not null)
                {
                    var url = p.DataUrlOrBase64.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                              || p.DataUrlOrBase64.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                        ? p.DataUrlOrBase64
                        : $"data:{p.MimeType ?? "image/png"};base64,{p.DataUrlOrBase64}";
                    arr.Add(new { image = url });
                }
            }
            if (arr.Count == 0) arr.Add(new { text = m.AsPlainText() });
            return new { role, content = arr };
        }

        return new { role, content = m.AsPlainText() };
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        string body; try { body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); } catch { body = ""; }
        throw new LlmProviderException(ProviderId, $"HTTP {(int)resp.StatusCode}: {body}", (int)resp.StatusCode);
    }
}
