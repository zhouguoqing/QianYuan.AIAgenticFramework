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

        var provider = ResolveProvider(req.Provider);
        if (provider is null)
            return NotFound(new { error = $"OpenAI-compatible provider '{req.Provider ?? _options.DefaultProviderId ?? "openai"}' is not configured." });
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = $"Provider '{provider.ProviderId}' has no API key." });

        var endpoint = string.Equals(mode, "image-to-image", StringComparison.OrdinalIgnoreCase)
            ? "images/edits"
            : "images/generations";

        using var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(5);
        http.BaseAddress = new Uri(provider.BaseUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);

        try
        {
            using var response = await SendGenerationRequestWithRetry(http, endpoint, req, provider, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, new
                {
                    error = ExtractProviderError(body),
                    model = ResolveImageModel(req, provider),
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
                Model = ResolveImageModel(req, provider),
                Url = url,
                Base64 = base64,
                Mime = "image/png",
                RevisedPrompt = image?["revised_prompt"]?.GetValue<string>(),
            });
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex) when (IsConnectionReset(ex))
        {
            _logger.LogError(ex, "images/generate upstream connection reset");
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "Image provider reset the connection before returning a response. Please retry with a shorter prompt or a smaller size.",
                model = ResolveImageModel(req, provider),
                provider = provider.ProviderId,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "images/generate failed");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
    }

    private OpenAIProviderOptions? ResolveProvider(string? providerId)
    {
        var desired = providerId ?? _options.DefaultProviderId ?? "openai";
        return _options.OpenAICompatProviders.FirstOrDefault(p =>
            string.Equals(p.ProviderId, desired, StringComparison.OrdinalIgnoreCase))
            ?? _options.OpenAICompatProviders.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.ApiKey))
            ?? _options.OpenAICompatProviders.FirstOrDefault();
    }

    private async Task<HttpResponseMessage> SendGenerationRequestWithRetry(
        HttpClient http,
        string endpoint,
        ImageGenerationRequest req,
        OpenAIProviderOptions provider,
        CancellationToken ct)
    {
        const int maxAttempts = 2;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SendGenerationRequest(http, endpoint, req, provider, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts && IsConnectionReset(ex))
            {
                _logger.LogWarning(ex, "Image provider reset the connection on attempt {Attempt}; retrying once", attempt);
            }
        }
    }

    private static async Task<HttpResponseMessage> SendGenerationRequest(
        HttpClient http,
        string endpoint,
        ImageGenerationRequest req,
        OpenAIProviderOptions provider,
        CancellationToken ct)
    {
        var model = ResolveImageModel(req, provider);
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

    private static string ResolveImageModel(ImageGenerationRequest req, OpenAIProviderOptions provider)
        => string.IsNullOrWhiteSpace(req.Model) ? provider.ImageModel ?? provider.DefaultModel : req.Model;

    private static bool SupportsResponseFormat(string model)
        => !model.StartsWith("gpt-image", StringComparison.OrdinalIgnoreCase);

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

public sealed class ImageGenerationRequest
{
    public string Mode { get; set; } = "text-to-image";
    public string Prompt { get; set; } = "";
    public ImagePart[]? Images { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? Size { get; set; }
}

public sealed class ImageGenerationResponse
{
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string? Url { get; set; }
    public string? Base64 { get; set; }
    public string Mime { get; set; } = "image/png";
    public string? RevisedPrompt { get; set; }
}
