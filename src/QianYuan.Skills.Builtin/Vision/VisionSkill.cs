using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;
using QianYuan.Core.Streaming;

namespace QianYuan.Skills.Builtin.Vision;

/// <summary>
/// Vision skill: describes an image (URL or base64) by delegating to a vision-capable LLM provider.
/// Tool: image_describe(image_url|image_base64, prompt?, provider?, model?).
///
/// This makes "image recognition" available as a discrete tool to any agent, not only the assistant's
/// own multimodal turn — useful in agent-as-tool scenarios where the parent agent forwards a URL.
/// </summary>
public sealed class VisionSkill : ISkill
{
    public string Id => "qianyuan.vision";
    public string Name => "Vision";
    public string Description => "Describe or analyze an image using a vision-capable LLM (Claude, GPT-4o, Gemini, Qwen-VL).";
    public IReadOnlyList<string> Tags => new[] { "image", "vision", "ocr", "photo", "screenshot", "picture" };
    public string? SystemPromptFragment => "If the user attached an image, you may call image_describe to get a detailed analysis when needed.";

    private static readonly ToolDefinition[] _tools =
    [
        new ToolDefinition
        {
            Name = "image_describe",
            Description = "Describe an image. Provide either image_url (http/https or data URL) OR image_base64+mime.",
            JsonSchema = "{\"type\":\"object\",\"properties\":{\"image_url\":{\"type\":\"string\",\"description\":\"HTTP(S) URL or data URL of the image.\"},\"image_base64\":{\"type\":\"string\",\"description\":\"Base64 image bytes.\"},\"mime\":{\"type\":\"string\",\"description\":\"MIME type if image_base64 is used, e.g. image/png.\"},\"prompt\":{\"type\":\"string\",\"description\":\"Optional question or instruction.\"},\"provider\":{\"type\":\"string\",\"description\":\"Optional provider id override.\"},\"model\":{\"type\":\"string\",\"description\":\"Optional model id override.\"}}}",
        }
    ];

    public ValueTask<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct = default)
        => ValueTask.FromResult<IReadOnlyList<ToolDefinition>>(_tools);

    public async ValueTask<SkillInvocationResult> InvokeAsync(string toolName, string argumentsJson, SkillInvocationContext context, CancellationToken ct = default)
    {
        if (toolName != "image_describe") return SkillInvocationResult.Error($"unknown tool '{toolName}'");

        var args = JsonNode.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson) ?? new JsonObject();
        var url = args["image_url"]?.GetValue<string>();
        var b64 = args["image_base64"]?.GetValue<string>();
        var mime = args["mime"]?.GetValue<string>() ?? "image/png";
        var prompt = args["prompt"]?.GetValue<string>() ?? "Describe this image in detail.";
        var providerId = args["provider"]?.GetValue<string>();
        var model = args["model"]?.GetValue<string>();

        if (string.IsNullOrEmpty(url) && string.IsNullOrEmpty(b64))
            return SkillInvocationResult.Error("provide image_url or image_base64");

        var registry = context.Services.GetRequiredService<ILlmProviderRegistry>();
        ILlmProvider provider;
        if (!string.IsNullOrEmpty(providerId))
        {
            provider = registry.Get(providerId) ?? throw new InvalidOperationException($"vision: provider '{providerId}' not registered");
        }
        else
        {
            provider = registry.List().FirstOrDefault(p => (p.Capabilities & LlmCapabilities.Vision) != 0) ?? registry.Default;
        }

        var imagePart = !string.IsNullOrEmpty(url)
            ? ContentPart.FromImageUrl(url!)
            : ContentPart.FromImageBase64(b64!, mime);

        var msg = new ChatMessage
        {
            Role = ChatRole.User,
            Parts = new[] { ContentPart.FromText(prompt), imagePart }
        };

        var resp = await provider.CompleteAsync(new ChatRequest
        {
            Messages = new[] { msg },
            Options = new GenerationOptions { Model = model, Temperature = 0.2f, MaxOutputTokens = 1024, Stream = false }
        }, ct).ConfigureAwait(false);

        var text = resp.Message.AsPlainText();
        return SkillInvocationResult.Ok(JsonSerializer.Serialize(new { description = text, model = resp.Model }), text);
    }
}
