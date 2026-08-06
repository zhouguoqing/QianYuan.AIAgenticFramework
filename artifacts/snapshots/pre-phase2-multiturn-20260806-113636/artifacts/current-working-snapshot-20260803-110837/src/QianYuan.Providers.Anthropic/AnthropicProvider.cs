using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Exceptions;
using QianYuan.Core.Models;
using QianYuan.Core.Streaming;

namespace QianYuan.Providers.Anthropic;

/// <summary>
/// Anthropic Claude provider implementing the Messages API. Supports:
///  - streaming (event-typed SSE)
///  - vision (image blocks)
///  - tool use (input streamed as JSON deltas)
///  - prompt caching (system + tools cache_control breakpoints)
///  - extended thinking (yields ThinkingDelta chunks)
/// </summary>
public sealed class AnthropicProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly AnthropicOptions _opts;
    private readonly ILogger<AnthropicProvider> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public AnthropicProvider(HttpClient http, AnthropicOptions opts, ILogger<AnthropicProvider> logger)
    {
        _http = http;
        _opts = opts;
        _logger = logger;
        Configure();
    }

    public string ProviderId => _opts.ProviderId;
    public string DefaultModel => _opts.DefaultModel;

    public LlmCapabilities Capabilities =>
        LlmCapabilities.Streaming | LlmCapabilities.Tools | LlmCapabilities.Vision |
        LlmCapabilities.PromptCaching | LlmCapabilities.Thinking | LlmCapabilities.ParallelToolCalls;

    private void Configure()
    {
        _http.Timeout = _opts.Timeout;
        if (_http.BaseAddress is null) _http.BaseAddress = new Uri(_opts.BaseUrl.TrimEnd('/') + "/");
        if (!_http.DefaultRequestHeaders.Contains("x-api-key"))
            _http.DefaultRequestHeaders.Add("x-api-key", _opts.ApiKey);
        if (!_http.DefaultRequestHeaders.Contains("anthropic-version"))
            _http.DefaultRequestHeaders.Add("anthropic-version", _opts.AnthropicVersion);
    }

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        var body = BuildBody(request, stream: false);
        using var resp = await _http.PostAsJsonAsync("v1/messages", body, JsonOpts, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadFromJsonAsync<JsonNode>(JsonOpts, ct).ConfigureAwait(false)!;

        var parts = new List<ContentPart>();
        var contentArr = json["content"]?.AsArray();
        if (contentArr is not null)
        {
            foreach (var block in contentArr)
            {
                var type = block?["type"]?.GetValue<string>();
                if (type == "text")
                    parts.Add(ContentPart.FromText(block?["text"]?.GetValue<string>() ?? ""));
                else if (type == "tool_use")
                {
                    var id = block?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
                    var name = block?["name"]?.GetValue<string>() ?? "";
                    var input = block?["input"]?.ToJsonString() ?? "{}";
                    parts.Add(ContentPart.ToolCall(id, name, input));
                }
            }
        }

        var usage = json["usage"];
        return new ChatResponse
        {
            Message = ChatMessage.Assistant(parts),
            FinishReason = json["stop_reason"]?.GetValue<string>(),
            Model = json["model"]?.GetValue<string>(),
            Usage = usage is null ? null : new TokenUsage(
                usage["input_tokens"]?.GetValue<int>() ?? 0,
                usage["output_tokens"]?.GetValue<int>() ?? 0,
                usage["cache_read_input_tokens"]?.GetValue<int>(),
                usage["cache_creation_input_tokens"]?.GetValue<int>())
        };
    }

    public async IAsyncEnumerable<StreamingChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildBody(request, stream: true);
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(body, options: JsonOpts)
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp, ct).ConfigureAwait(false);
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        var blockState = new Dictionary<int, (string type, string? id, string? name, StringBuilder argBuf)>();
        string? model = null;
        TokenUsage? usage = null;
        string? finishReason = null;
        var startedEmitted = false;

        await foreach (var (evt, data) in SseReader.ReadAsync(stream, ct))
        {
            JsonNode? node;
            try { node = JsonNode.Parse(data); }
            catch { continue; }
            if (node is null) continue;

            switch (evt)
            {
                case "message_start":
                {
                    var msg = node["message"];
                    model = msg?["model"]?.GetValue<string>();
                    if (!startedEmitted) { yield return StreamingChunk.Start(model); startedEmitted = true; }
                    var u = msg?["usage"];
                    if (u is not null)
                    {
                        usage = new TokenUsage(
                            u["input_tokens"]?.GetValue<int>() ?? 0,
                            u["output_tokens"]?.GetValue<int>() ?? 0,
                            u["cache_read_input_tokens"]?.GetValue<int>(),
                            u["cache_creation_input_tokens"]?.GetValue<int>());
                    }
                    break;
                }
                case "content_block_start":
                {
                    var idx = node["index"]?.GetValue<int>() ?? 0;
                    var block = node["content_block"];
                    var type = block?["type"]?.GetValue<string>() ?? "text";
                    if (type == "tool_use")
                    {
                        var id = block?["id"]?.GetValue<string>() ?? $"tu_{idx}_{Guid.NewGuid():N}";
                        var name = block?["name"]?.GetValue<string>() ?? "";
                        blockState[idx] = (type, id, name, new StringBuilder());
                        yield return new StreamingChunk
                        {
                            Kind = StreamingChunkKind.ToolCallStart,
                            ToolCallId = id, ToolName = name
                        };
                    }
                    else
                    {
                        blockState[idx] = (type, null, null, new StringBuilder());
                    }
                    break;
                }
                case "content_block_delta":
                {
                    var idx = node["index"]?.GetValue<int>() ?? 0;
                    var d = node["delta"];
                    var t = d?["type"]?.GetValue<string>();
                    if (t == "text_delta")
                    {
                        var txt = d?["text"]?.GetValue<string>() ?? "";
                        if (txt.Length > 0) yield return StreamingChunk.OfText(txt);
                    }
                    else if (t == "thinking_delta")
                    {
                        var txt = d?["thinking"]?.GetValue<string>() ?? "";
                        if (txt.Length > 0) yield return StreamingChunk.Thinking(txt);
                    }
                    else if (t == "input_json_delta")
                    {
                        var partial = d?["partial_json"]?.GetValue<string>() ?? "";
                        if (blockState.TryGetValue(idx, out var st) && partial.Length > 0)
                        {
                            st.argBuf.Append(partial);
                            yield return new StreamingChunk
                            {
                                Kind = StreamingChunkKind.ToolCallArgsDelta,
                                ToolCallId = st.id, ToolName = st.name,
                                ToolArgsJson = partial
                            };
                        }
                    }
                    break;
                }
                case "content_block_stop":
                {
                    var idx = node["index"]?.GetValue<int>() ?? 0;
                    if (blockState.TryGetValue(idx, out var st) && st.type == "tool_use")
                    {
                        yield return new StreamingChunk
                        {
                            Kind = StreamingChunkKind.ToolCallEnd,
                            ToolCallId = st.id, ToolName = st.name,
                            ToolArgsJson = st.argBuf.Length == 0 ? "{}" : st.argBuf.ToString()
                        };
                    }
                    break;
                }
                case "message_delta":
                {
                    var d = node["delta"];
                    var stop = d?["stop_reason"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(stop)) finishReason = stop;
                    var u = node["usage"];
                    if (u is not null)
                    {
                        usage = new TokenUsage(
                            usage?.InputTokens ?? 0,
                            u["output_tokens"]?.GetValue<int>() ?? usage?.OutputTokens ?? 0,
                            usage?.CacheReadTokens,
                            usage?.CacheWriteTokens);
                        yield return new StreamingChunk { Kind = StreamingChunkKind.Usage, Usage = usage };
                    }
                    break;
                }
                case "message_stop":
                    break;
                case "error":
                {
                    var msg = node["error"]?["message"]?.GetValue<string>() ?? "unknown error";
                    yield return StreamingChunk.Error(msg);
                    break;
                }
            }
        }

        yield return StreamingChunk.End(finishReason ?? "stop", usage);
    }

    private object BuildBody(ChatRequest request, bool stream)
    {
        // Split system message(s) out — Claude takes them as a top-level "system" field.
        var systemText = string.Join("\n\n",
            request.Messages.Where(m => m.Role == ChatRole.System).Select(m => m.AsPlainText()));

        var msgs = new List<object>();
        foreach (var m in request.Messages.Where(m => m.Role != ChatRole.System))
            msgs.Add(SerializeMessage(m));

        var body = new Dictionary<string, object?>
        {
            ["model"] = request.Options.Model ?? _opts.DefaultModel,
            ["max_tokens"] = request.Options.MaxOutputTokens ?? 4096,
            ["messages"] = msgs,
            ["stream"] = stream,
        };

        if (!string.IsNullOrEmpty(systemText))
        {
            if (request.Options.EnablePromptCaching)
            {
                body["system"] = new object[]
                {
                    new { type = "text", text = systemText, cache_control = new { type = "ephemeral" } }
                };
            }
            else body["system"] = systemText;
        }

        if (request.Options.Temperature is not null) body["temperature"] = request.Options.Temperature;
        if (request.Options.TopP is not null) body["top_p"] = request.Options.TopP;
        if (request.Options.StopSequences is { Count: > 0 }) body["stop_sequences"] = request.Options.StopSequences;

        if (request.Tools is { Count: > 0 })
        {
            var toolsArr = new List<object>();
            for (int i = 0; i < request.Tools.Count; i++)
            {
                var t = request.Tools[i];
                var obj = new Dictionary<string, object?>
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["input_schema"] = JsonNode.Parse(t.JsonSchema)
                };
                // Cache only the last tool block so the whole tool list is cached as a unit.
                if (request.Options.EnablePromptCaching && i == request.Tools.Count - 1)
                    obj["cache_control"] = new { type = "ephemeral" };
                toolsArr.Add(obj);
            }
            body["tools"] = toolsArr;
            if (request.Options.ToolChoice is not null && request.Options.ToolChoice != "auto")
            {
                body["tool_choice"] = request.Options.ToolChoice switch
                {
                    "none" => new { type = "none" },
                    "required" => new { type = "any" },
                    _ => new { type = "tool", name = request.Options.ToolChoice }
                };
            }
        }

        if (_opts.EnableExtendedThinking && _opts.ThinkingBudgetTokens is int budget && budget > 0)
        {
            body["thinking"] = new { type = "enabled", budget_tokens = budget };
        }

        return body;
    }

    private static object SerializeMessage(ChatMessage m)
    {
        var role = m.Role == ChatRole.Assistant ? "assistant" : "user";

        // Tool result block — Claude expects role=user content=[{type:tool_result,...}].
        if (m.Role == ChatRole.Tool)
        {
            var resultPart = m.Parts.FirstOrDefault(p => p.Kind == ContentKind.ToolResult);
            return new
            {
                role = "user",
                content = new object[]
                {
                    new
                    {
                        type = "tool_result",
                        tool_use_id = resultPart?.ToolCallId ?? "",
                        content = resultPart?.JsonPayload ?? resultPart?.Text ?? ""
                    }
                }
            };
        }

        // Assistant turn with tool_use blocks.
        if (m.Role == ChatRole.Assistant && m.Parts.Any(p => p.Kind == ContentKind.ToolCall))
        {
            var blocks = new List<object>();
            foreach (var p in m.Parts)
            {
                if (p.Kind == ContentKind.Text && p.Text is not null)
                    blocks.Add(new { type = "text", text = p.Text });
                else if (p.Kind == ContentKind.ToolCall)
                    blocks.Add(new
                    {
                        type = "tool_use",
                        id = p.ToolCallId,
                        name = p.Name,
                        input = JsonNode.Parse(string.IsNullOrEmpty(p.JsonPayload) ? "{}" : p.JsonPayload)
                    });
            }
            return new { role, content = blocks };
        }

        // Multimodal user content.
        if (m.Parts.Any(p => p.Kind == ContentKind.Image))
        {
            var blocks = new List<object>();
            foreach (var p in m.Parts)
            {
                if (p.Kind == ContentKind.Text && p.Text is not null)
                    blocks.Add(new { type = "text", text = p.Text });
                else if (p.Kind == ContentKind.Image && p.DataUrlOrBase64 is not null)
                {
                    if (p.DataUrlOrBase64.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        blocks.Add(new { type = "image", source = new { type = "url", url = p.DataUrlOrBase64 } });
                    else
                        blocks.Add(new
                        {
                            type = "image",
                            source = new
                            {
                                type = "base64",
                                media_type = p.MimeType ?? "image/png",
                                data = StripDataUrl(p.DataUrlOrBase64)
                            }
                        });
                }
            }
            return new { role, content = blocks };
        }

        return new { role, content = m.AsPlainText() };
    }

    private static string StripDataUrl(string s)
    {
        var i = s.IndexOf(',');
        return s.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && i > 0 ? s[(i + 1)..] : s;
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
