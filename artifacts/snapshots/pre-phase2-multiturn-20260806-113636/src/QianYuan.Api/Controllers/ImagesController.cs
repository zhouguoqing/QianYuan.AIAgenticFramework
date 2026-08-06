using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using QianYuan.Api.Configuration;

namespace QianYuan.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ImagesController : ControllerBase
{
    private readonly QianYuanApiOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ImagesController> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public ImagesController(
        IOptions<QianYuanApiOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<ImagesController> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] ImageGenerationRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Prompt))
            return BadRequest(new { error = "Prompt is required." });

        var mode = string.IsNullOrWhiteSpace(req.Mode) ? "text-to-image" : req.Mode;
        if (!string.Equals(mode, "text-to-image", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mode, "image-to-image", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Mode must be text-to-image or image-to-image." });

        if (string.Equals(mode, "image-to-image", StringComparison.OrdinalIgnoreCase)
            && req.Images is not { Length: > 0 })
            return BadRequest(new { error = "At least one input image is required for image-to-image." });

        var provider = ResolveImageProvider(req.Provider, req.Model);
        if (provider is null)
            return NotFound(new { error = $"OpenAI-compatible image provider '{req.Provider ?? "auto"}' is not configured." });
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = $"Provider '{provider.ProviderId}' has no API key." });

        var model = ResolveImageModel(req, provider);

        using var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(5);
        http.BaseAddress = new Uri(provider.BaseUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);

        try
        {
            var promptOptimization = await OptimizeImagePromptAsync(req, mode, ct).ConfigureAwait(false);
            var effectiveRequest = promptOptimization.OptimizedPrompt is null
                ? req
                : CloneWithPrompt(req, promptOptimization.OptimizedPrompt);
            var (response, resolvedModel) = await SendGenerationRequestWithRetry(http, effectiveRequest, provider, ct).ConfigureAwait(false);
            model = resolvedModel;
            using var responseScope = response;
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, new
                {
                    error = ExtractProviderError(body),
                    model,
                    provider = provider.ProviderId,
                });

            var parsed = JsonNode.Parse(body);
            var image = parsed?["data"]?.AsArray().FirstOrDefault();
            var url = image?["url"]?.GetValue<string>();
            var base64 = image?["b64_json"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(base64))
                return StatusCode(StatusCodes.Status502BadGateway, new { error = "Image provider returned no image data." });

            return Ok(new ImageGenerationResponse
            {
                Provider = provider.ProviderId,
                Model = model,
                Url = url,
                Base64 = base64,
                Mime = "image/png",
                RevisedPrompt = image?["revised_prompt"]?.GetValue<string>(),
                OptimizedPrompt = promptOptimization.OptimizedPrompt,
                PromptOptimizerProvider = promptOptimization.ProviderId,
                PromptOptimizerModel = promptOptimization.Model,
                PromptOptimizationSkipped = promptOptimization.Skipped,
                PromptOptimizationError = promptOptimization.Error,
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex) when (IsConnectionReset(ex))
        {
            _logger.LogError(ex, "images/generate upstream connection reset");
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "Image provider reset the connection before returning a response. Please retry with a shorter prompt or a smaller size.",
                model,
                provider = provider.ProviderId,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "images/generate failed");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
    }

    private OpenAIProviderOptions? ResolveImageProvider(string? providerId, string? model)
    {
        if (!string.IsNullOrWhiteSpace(providerId))
        {
            var requested = _options.OpenAICompatProviders.FirstOrDefault(p =>
                string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
            if (requested is not null && IsImageProvider(requested, model)) return requested;
        }

        return _options.OpenAICompatProviders.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.ApiKey) && IsImageProvider(p, model))
            ?? _options.OpenAICompatProviders.FirstOrDefault();
    }

    private static bool IsImageProvider(OpenAIProviderOptions provider, string? model)
    {
        if (!string.IsNullOrWhiteSpace(provider.ImageModel)) return true;
        if (!string.IsNullOrWhiteSpace(model) && model.StartsWith("gpt-image", StringComparison.OrdinalIgnoreCase)) return true;
        return provider.Models.Any(m => m.StartsWith("gpt-image", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<PromptOptimizationResult> OptimizeImagePromptAsync(ImageGenerationRequest req, string mode, CancellationToken ct)
    {
        if (req.OptimizePrompt is false)
            return PromptOptimizationResult.Skip();

        var optimizer = ResolvePromptOptimizerProvider();
        if (optimizer is null || string.IsNullOrWhiteSpace(optimizer.ApiKey) || string.IsNullOrWhiteSpace(optimizer.DefaultModel))
            return PromptOptimizationResult.Skip("Prompt optimizer provider is not configured.");

        try
        {
            using var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(60);
            http.BaseAddress = new Uri(optimizer.BaseUrl.TrimEnd('/') + "/");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", optimizer.ApiKey);

            var referenceNote = string.Equals(mode, "image-to-image", StringComparison.OrdinalIgnoreCase)
                ? $"The user provided {req.Images?.Length ?? 0} reference image(s). The image model will receive them separately as reference_images; preserve relevant subject, composition, and style unless the user asks to change them."
                : "No reference image is provided.";
            var body = new Dictionary<string, object?>
            {
                ["model"] = optimizer.DefaultModel,
                ["messages"] = new object[]
                {
                    new { role = "system", content = "You optimize prompts for image generation. Return only one polished image prompt, no markdown, no explanations. Preserve the user's visual intent, key objects, style, composition, colors, and constraints. Prefer clear visual language in English. Do not render the raw user prompt itself as visible text. Only include visible lettering when the user explicitly asks for text to appear in the image with words such as text, says, label, title, logo text, quote, 写着, 文字, 标题, or 文案. If the prompt contains JSON Unicode escape sequences like \\u4e00, decode them mentally before rewriting." },
                    new { role = "user", content = $"Mode: {mode}\n{referenceNote}\nOriginal user prompt, escaped to preserve non-ASCII text through the gateway:\n{EscapeForPromptOptimizer(req.Prompt)}\n\nRewrite it into a production-ready prompt for an image generation model." }
                }
            };
            if (optimizer.SupportsSamplingParams)
                body["temperature"] = 0.2;

            var json = JsonSerializer.Serialize(body, JsonOpts);
            using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await http.PostAsync("chat/completions", content, ct).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var error = ExtractProviderError(responseBody);
                _logger.LogWarning("Prompt optimization failed via {Provider}/{Model}: HTTP {Status} {Error}", optimizer.ProviderId, optimizer.DefaultModel, (int)response.StatusCode, error);
                return PromptOptimizationResult.Fail(optimizer.ProviderId, optimizer.DefaultModel, error);
            }

            var optimized = JsonNode.Parse(responseBody)?["choices"]?.AsArray().FirstOrDefault()?["message"]?["content"]?.GetValue<string>();
            optimized = CleanOptimizedPrompt(optimized);
            if (string.IsNullOrWhiteSpace(optimized))
                return PromptOptimizationResult.Fail(optimizer.ProviderId, optimizer.DefaultModel, "Prompt optimizer returned empty content.");

            return PromptOptimizationResult.Success(optimizer.ProviderId, optimizer.DefaultModel, optimized);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Prompt optimization failed; falling back to original prompt");
            return PromptOptimizationResult.Fail(optimizer.ProviderId, optimizer.DefaultModel, ex.Message);
        }
    }

    private OpenAIProviderOptions? ResolvePromptOptimizerProvider()
        => _options.OpenAICompatProviders.FirstOrDefault(p =>
                string.Equals(p.ProviderId, "openai-chat", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(p.ApiKey))
            ?? _options.OpenAICompatProviders.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(p.ApiKey)
                && !IsImageProvider(p, p.DefaultModel));

    private static string? CleanOptimizedPrompt(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return null;
        var value = prompt.Trim();
        if (value.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = value.IndexOf('\n');
            var lastFence = value.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
                value = value[(firstNewLine + 1)..lastFence].Trim();
        }
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            value = value[1..^1].Trim();
        return value.Length > 4000 ? value[..4000] : value;
    }

    private static string EscapeForPromptOptimizer(string prompt)
    {
        var sb = new StringBuilder(prompt.Length);
        foreach (var ch in prompt)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 32 || ch > 126) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    private static ImageGenerationRequest CloneWithPrompt(ImageGenerationRequest req, string prompt) => new()
    {
        Mode = req.Mode,
        Prompt = prompt,
        Images = req.Images,
        Provider = req.Provider,
        Model = req.Model,
        Size = req.Size,
        OptimizePrompt = req.OptimizePrompt,
    };

    private async Task<(HttpResponseMessage Response, string Model)> SendGenerationRequestWithRetry(
        HttpClient http,
        ImageGenerationRequest req,
        OpenAIProviderOptions provider,
        CancellationToken ct)
    {
        var candidateModels = ImageGenerationModelResolver.GetCandidateModels(req, provider);
        for (var attempt = 0; attempt < candidateModels.Count; attempt++)
        {
            var model = candidateModels[attempt];
            var endpoint = string.Equals(req.Mode, "image-to-image", StringComparison.OrdinalIgnoreCase)
                && !UsesGenerationEndpointForReferenceImages(model)
                ? "images/edits"
                : "images/generations";
            try
            {
                var response = await SendGenerationRequest(http, endpoint, req, model, ct).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (ShouldRetryWithFallbackModel(response.StatusCode, body, model, attempt, candidateModels.Count))
                {
                    response.Dispose();
                    _logger.LogWarning("Image provider rejected model {Model} with {StatusCode}; retrying with fallback model", model, response.StatusCode);
                    continue;
                }

                return (response, model);
            }
            catch (HttpRequestException ex) when (attempt < candidateModels.Count - 1 && IsConnectionReset(ex))
            {
                _logger.LogWarning(ex, "Image provider reset the connection on attempt {Attempt}; retrying once", attempt + 1);
            }
        }

        throw new HttpRequestException("Image generation failed after exhausting all candidate models.");
    }

    private static async Task<HttpResponseMessage> SendGenerationRequest(
        HttpClient http,
        string endpoint,
        ImageGenerationRequest req,
        string model,
        CancellationToken ct)
    {
        var supportsResponseFormat = SupportsResponseFormat(model);
        if (string.Equals(endpoint, "images/edits", StringComparison.OrdinalIgnoreCase))
        {
            var form = new MultipartFormDataContent
            {
                { new StringContent(model), "model" },
                { new StringContent(req.Prompt), "prompt" },
                { new StringContent(req.Size ?? "1024x1024"), "size" },
            };
            if (supportsResponseFormat)
                form.Add(new StringContent("b64_json"), "response_format");
            foreach (var image in req.Images ?? Array.Empty<ImagePart>())
            {
                var (bytes, mime) = DecodeImage(image);
                var file = new ByteArrayContent(bytes);
                file.Headers.ContentType = new MediaTypeHeaderValue(mime);
                form.Add(file, "image", "input.png");
            }
            using var editRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = form,
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            };
            editRequest.Headers.ConnectionClose = true;
            editRequest.Headers.ExpectContinue = false;
            editRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return await http.SendAsync(editRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["prompt"] = req.Prompt,
            ["size"] = req.Size ?? "1024x1024",
        };
        if (string.Equals(req.Mode, "image-to-image", StringComparison.OrdinalIgnoreCase)
            && UsesGenerationEndpointForReferenceImages(model))
        {
            body["reference_images"] = (req.Images ?? Array.Empty<ImagePart>())
                .Select(ToDataUrl)
                .ToArray();
        }
        if (supportsResponseFormat)
        {
            body["n"] = 1;
            body["response_format"] = "b64_json";
        }
        var json = JsonSerializer.Serialize(body, JsonOpts);
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = content,
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        request.Headers.ConnectionClose = true;
        request.Headers.ExpectContinue = false;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
    }

    private static (byte[] Bytes, string Mime) DecodeImage(ImagePart image)
    {
        var mime = image.Mime ?? "image/png";
        var data = image.Base64;
        if (string.IsNullOrWhiteSpace(data) && image.Url?.StartsWith("data:", StringComparison.OrdinalIgnoreCase) == true)
        {
            var comma = image.Url.IndexOf(',');
            if (comma > 0)
            {
                var header = image.Url[..comma];
                var marker = "data:";
                var semi = header.IndexOf(';');
                if (semi > marker.Length) mime = header[marker.Length..semi];
                data = image.Url[(comma + 1)..];
            }
        }
        if (string.IsNullOrWhiteSpace(data))
            throw new InvalidOperationException("Only base64/data-url images are supported for image-to-image edits.");
        return (Convert.FromBase64String(data), mime);
    }

    private static string ToDataUrl(ImagePart image)
    {
        if (!string.IsNullOrWhiteSpace(image.Url))
        {
            if (image.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return image.Url;
            if (image.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return image.Url;
        }

        var (bytes, mime) = DecodeImage(image);
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }

    private static string ResolveImageModel(ImageGenerationRequest req, OpenAIProviderOptions provider)
        => string.IsNullOrWhiteSpace(req.Model) ? provider.ImageModel ?? provider.DefaultModel : req.Model;

    private static bool ShouldRetryWithFallbackModel(HttpStatusCode statusCode, string body, string model, int attempt, int candidateCount)
    {
        if (attempt >= candidateCount - 1 || !IsGptImageModel(model))
            return false;

        if (statusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.InternalServerError))
            return false;

        var error = body.ToLowerInvariant();
        return error.Contains("model", StringComparison.Ordinal)
            || error.Contains("not found", StringComparison.Ordinal)
            || error.Contains("unsupported", StringComparison.Ordinal)
            || error.Contains("gpt-image", StringComparison.Ordinal)
            || error.Contains("internal error", StringComparison.Ordinal);
    }

    private static bool IsGptImageModel(string model)
        => model.StartsWith("gpt-image", StringComparison.OrdinalIgnoreCase);

    private static bool SupportsResponseFormat(string model)
        => !IsGptImageModel(model);

    private static bool UsesGenerationEndpointForReferenceImages(string model)
        => model.StartsWith("gpt-image-2", StringComparison.OrdinalIgnoreCase);

    private static bool IsConnectionReset(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException!)
        {
            if (current is SocketException { SocketErrorCode: SocketError.ConnectionReset })
                return true;
            if (current is IOException io
                && io.Message.Contains("Connection reset", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string ExtractProviderError(string body)
    {
        try
        {
            var parsed = JsonNode.Parse(body);
            return parsed?["error"]?["message"]?.GetValue<string>()
                ?? parsed?["message"]?.GetValue<string>()
                ?? body;
        }
        catch (JsonException)
        {
            return body;
        }
    }
}


public static class ImageGenerationModelResolver
{
    public static IReadOnlyList<string> GetCandidateModels(ImageGenerationRequest req, OpenAIProviderOptions provider)
    {
        var requestedModel = req.Model;
        var configuredModel = provider.ImageModel ?? provider.DefaultModel;
        if (!string.IsNullOrWhiteSpace(requestedModel))
            return string.Equals(requestedModel, "gpt-image-2", StringComparison.OrdinalIgnoreCase)
                ? new[] { requestedModel, "gpt-image-1" }
                : new[] { requestedModel };

        if (string.IsNullOrWhiteSpace(configuredModel))
            return Array.Empty<string>();

        return string.Equals(configuredModel, "gpt-image-2", StringComparison.OrdinalIgnoreCase)
            ? new[] { configuredModel, "gpt-image-1" }
            : string.Equals(configuredModel, "gpt-image-1", StringComparison.OrdinalIgnoreCase)
                ? new[] { configuredModel, "gpt-image-2" }
                : new[] { configuredModel };
    }
}

public sealed class ImageGenerationRequest
{
    public string Mode { get; set; } = "text-to-image";
    public string Prompt { get; set; } = "";
    public ImagePart[]? Images { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? Size { get; set; }
    public bool? OptimizePrompt { get; set; }
}

public sealed class ImageGenerationResponse
{
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string? Url { get; set; }
    public string? Base64 { get; set; }
    public string Mime { get; set; } = "image/png";
    public string? RevisedPrompt { get; set; }
    public string? OptimizedPrompt { get; set; }
    public string? PromptOptimizerProvider { get; set; }
    public string? PromptOptimizerModel { get; set; }
    public bool PromptOptimizationSkipped { get; set; }
    public string? PromptOptimizationError { get; set; }
}

internal sealed record PromptOptimizationResult(
    string? OptimizedPrompt,
    string? ProviderId,
    string? Model,
    bool Skipped,
    string? Error)
{
    public static PromptOptimizationResult Success(string providerId, string model, string optimizedPrompt)
        => new(optimizedPrompt, providerId, model, false, null);

    public static PromptOptimizationResult Skip(string? error = null)
        => new(null, null, null, true, error);

    public static PromptOptimizationResult Fail(string providerId, string model, string error)
        => new(null, providerId, model, true, error);
}
