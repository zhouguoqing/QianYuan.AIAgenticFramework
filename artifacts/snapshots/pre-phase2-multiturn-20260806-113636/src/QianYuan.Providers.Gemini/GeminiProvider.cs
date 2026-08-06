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

namespace QianYuan.Providers.Gemini;

/// <summary>
/// Google Gemini provider using v1beta generateContent / streamGenerateContent endpoints.
/// Handles text + vision (inlineData) + function calling.
///
/// Streaming endpoint returns a JSON array streamed as concatenated objects (alt=sse). We use alt=sse
/// to get standard SSE framing rather than parsing a JSON array prefix.
/// </summary>
public sealed class GeminiProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly GeminiOptions _opts;
    private readonly ILogger<GeminiProvider> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public GeminiProvider(HttpClient http, GeminiOptions opts, ILogger<GeminiProvider> logger)
    {
        _http = http; _opts = opts; _logger = logger;
        Configure();
    }

    public string ProviderId => _opts.ProviderId;
    public string DefaultModel => _opts.DefaultModel;

    public LlmCapabilities Capabilities =>
        LlmCapabilities.Streaming | LlmCapabilities.Tools | LlmCapabilities.Vision |
        LlmCapabilities.JsonMode | LlmCapabilities.Thinking;

    private void Configure()
    {
        _http.Timeout = _opts.Timeout;
        if (_http.BaseAddress is null) _http.BaseAddress = new Uri(_opts.BaseUrl.TrimEnd('/') + "/");
    }

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        var model = request.Options.Model ?? _opts.DefaultModel;
        var body = BuildBody(request);
        var url = $"{_opts.ApiVersion}/models/{model}:generateContent?key={_opts.ApiKey}";
        using var resp = await _http.PostAsJsonAsync(url, body, JsonOpts, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadFromJsonAsync<JsonNode>(JsonOpts, ct).ConfigureAwait(false)!;

        var candidate = json["candidates"]?[0];
        var parts = ExtractParts(candidate?["content"]);
        var usage = json["usageMetadata"];
        return new ChatResponse
        {
            Message = ChatMessage.Assistant(parts),
            FinishReason = candidate?["finishReason"]?.GetValue<string>(),
            Model = model,
            Usage = usage is null ? null : new TokenUsage(
                usage["promptTokenCount"]?.GetValue<int>() ?? 0,
                usage["candidatesTokenCount"]?.GetValue<int>() ?? 0,
                usage["cachedContentTokenCount"]?.GetValue<int>())
        };
    }

    public async IAsyncEnumerable<StreamingChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = request.Options.Model ?? _opts.DefaultModel;
        var body = BuildBody(request);
        var url = $"{_opts.ApiVersion}/models/{model}:streamGenerateContent?alt=sse&key={_opts.ApiKey}";

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        { Content = JsonContent.Create(body, options: JsonOpts) };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp, ct).ConfigureAwait(false);
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        var startedEmitted = false;
        string? finishReason = null;
        TokenUsage? usage = null;

        await foreach (var payload in SseReader.ReadAsync(stream, ct))
        {
            if (string.IsNullOrWhiteSpace(payload) || payload == "[DONE]") continue;

            JsonNode? node; try { node = JsonNode.Parse(payload); } catch { continue; }
            if (node is null) continue;

            if (!startedEmitted) { yield return StreamingChunk.Start(model); startedEmitted = true; }

            var candidate = node["candidates"]?[0];
            var content = candidate?["content"];
            if (content is not null)
            {
                var partsArr = content["parts"]?.AsArray();
                if (partsArr is not null)
                {
                    foreach (var p in partsArr)
                    {
                        var text = p?["text"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(text)) yield return StreamingChunk.OfText(text);

                        var fc = p?["functionCall"];
                        if (fc is not null)
                        {
                            var name = fc["name"]?.GetValue<string>() ?? "";
                            var args = fc["args"]?.ToJsonString() ?? "{}";
                            var id = $"fc_{Guid.NewGuid():N}";
                            yield return new StreamingChunk
                            {
                                Kind = StreamingChunkKind.ToolCallStart,
                                ToolCallId = id, ToolName = name, ToolArgsJson = args
                            };
                            yield return new StreamingChunk
                            {
                                Kind = StreamingChunkKind.ToolCallEnd,
                                ToolCallId = id, ToolName = name, ToolArgsJson = args
                            };
                        }
                    }
                }
            }

            var fr = candidate?["finishReason"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(fr)) finishReason = fr;

            var u = node["usageMetadata"];
            if (u is not null)
            {
                usage = new TokenUsage(
                    u["promptTokenCount"]?.GetValue<int>() ?? 0,
                    u["candidatesTokenCount"]?.GetValue<int>() ?? 0,
                    u["cachedContentTokenCount"]?.GetValue<int>());
            }
        }

        yield return StreamingChunk.End(finishReason ?? "stop", usage);
    }

    private static List<ContentPart> ExtractParts(JsonNode? content)
    {
        var list = new List<ContentPart>();
        var arr = content?["parts"]?.AsArray();
        if (arr is null) return list;
        foreach (var p in arr)
        {
            var text = p?["text"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(text)) list.Add(ContentPart.FromText(text));
            var fc = p?["functionCall"];
            if (fc is not null)
            {
                list.Add(ContentPart.ToolCall(
                    $"fc_{Guid.NewGuid():N}",
                    fc["name"]?.GetValue<string>() ?? "",
                    fc["args"]?.ToJsonString() ?? "{}"));
            }
        }
        return list;
    }

    private object BuildBody(ChatRequest request)
    {
        // System messages -> systemInstruction
        var systemText = string.Join("\n\n",
            request.Messages.Where(m => m.Role == ChatRole.System).Select(m => m.AsPlainText()));

        var contents = new List<object>();
        foreach (var m in request.Messages.Where(m => m.Role != ChatRole.System))
            contents.Add(SerializeMessage(m));

        var body = new Dictionary<string, object?>
        {
            ["contents"] = contents,
        };
        if (!string.IsNullOrEmpty(systemText))
            body["systemInstruction"] = new { role = "system", parts = new[] { new { text = systemText } } };

        var genCfg = new Dictionary<string, object?>();
        if (request.Options.Temperature is not null) genCfg["temperature"] = request.Options.Temperature;
        if (request.Options.TopP is not null) genCfg["topP"] = request.Options.TopP;
        if (request.Options.MaxOutputTokens is not null) genCfg["maxOutputTokens"] = request.Options.MaxOutputTokens;
        if (request.Options.StopSequences is { Count: > 0 }) genCfg["stopSequences"] = request.Options.StopSequences;
        if (genCfg.Count > 0) body["generationConfig"] = genCfg;

        if (request.Tools is { Count: > 0 })
        {
            var decls = request.Tools.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                parameters = JsonNode.Parse(t.JsonSchema)
            }).ToArray();
            body["tools"] = new object[] { new { functionDeclarations = decls } };
            if (request.Options.ToolChoice is { } tc && tc != "auto")
            {
                body["toolConfig"] = new
                {
                    functionCallingConfig = tc switch
                    {
                        "none" => new { mode = "NONE" },
                        "required" => new { mode = "ANY" },
                        _ => (object)new { mode = "ANY", allowedFunctionNames = new[] { tc } }
                    }
                };
            }
        }

        return body;
    }

    private static object SerializeMessage(ChatMessage m)
    {
        var role = m.Role switch
        {
            ChatRole.Assistant => "model",
            ChatRole.Tool => "user",
            _ => "user"
        };

        if (m.Role == ChatRole.Tool)
        {
            var rp = m.Parts.FirstOrDefault(p => p.Kind == ContentKind.ToolResult);
            JsonNode? respNode;
            try { respNode = JsonNode.Parse(rp?.JsonPayload ?? "{}"); }
            catch { respNode = JsonNode.Parse("{}"); }
            return new
            {
                role = "user",
                parts = new object[]
                {
                    new
                    {
                        functionResponse = new
                        {
                            name = rp?.Name ?? rp?.ToolCallId ?? "tool",
                            response = respNode
                        }
                    }
                }
            };
        }

        var parts = new List<object>();
        foreach (var p in m.Parts)
        {
            switch (p.Kind)
            {
                case ContentKind.Text:
                    if (!string.IsNullOrEmpty(p.Text)) parts.Add(new { text = p.Text });
                    break;
                case ContentKind.Image when p.DataUrlOrBase64 is not null:
                    if (p.DataUrlOrBase64.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        parts.Add(new { fileData = new { fileUri = p.DataUrlOrBase64, mimeType = p.MimeType ?? "image/png" } });
                    else
                        parts.Add(new { inlineData = new { mimeType = p.MimeType ?? "image/png", data = StripDataUrl(p.DataUrlOrBase64) } });
                    break;
                case ContentKind.ToolCall:
                    JsonNode? args;
                    try { args = JsonNode.Parse(string.IsNullOrEmpty(p.JsonPayload) ? "{}" : p.JsonPayload); }
                    catch { args = JsonNode.Parse("{}"); }
                    parts.Add(new { functionCall = new { name = p.Name, args } });
                    break;
            }
        }
        if (parts.Count == 0) parts.Add(new { text = m.AsPlainText() });
        return new { role, parts };
    }

    private static string StripDataUrl(string s)
    {
        var i = s.IndexOf(',');
        return s.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && i > 0 ? s[(i + 1)..] : s;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        string body; try { body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); } catch { body = ""; }
        throw new LlmProviderException(ProviderId, $"HTTP {(int)resp.StatusCode}: {body}", (int)resp.StatusCode);
    }
}
