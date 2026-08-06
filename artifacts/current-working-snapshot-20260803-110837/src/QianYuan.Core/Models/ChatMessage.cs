using System.Text.Json.Serialization;

namespace QianYuan.Core.Models;

/// <summary>Role of a chat message.</summary>
public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool
}

/// <summary>Kind of content carried by a message part.</summary>
public enum ContentKind
{
    Text,
    Image,
    Audio,
    File,
    ToolCall,
    ToolResult
}

/// <summary>One part of a multimodal chat message.</summary>
public sealed class ContentPart
{
    public ContentKind Kind { get; init; } = ContentKind.Text;

    /// <summary>Text payload (for Text kind, or auxiliary text on tool result).</summary>
    public string? Text { get; init; }

    /// <summary>For Image/Audio/File: either inline base64 data or a remote URL.</summary>
    public string? DataUrlOrBase64 { get; init; }

    /// <summary>MIME type for binary parts, e.g. image/png.</summary>
    public string? MimeType { get; init; }

    /// <summary>For ToolCall: tool name. For ToolResult: tool call id this responds to.</summary>
    public string? Name { get; init; }

    /// <summary>Tool call identifier - links ToolCall to its ToolResult.</summary>
    public string? ToolCallId { get; init; }

    /// <summary>For ToolCall: JSON arguments. For ToolResult: JSON content.</summary>
    public string? JsonPayload { get; init; }

    public static ContentPart FromText(string text) => new() { Kind = ContentKind.Text, Text = text };

    public static ContentPart FromImageUrl(string url, string? mime = null) =>
        new() { Kind = ContentKind.Image, DataUrlOrBase64 = url, MimeType = mime };

    public static ContentPart FromImageBase64(string base64, string mime) =>
        new() { Kind = ContentKind.Image, DataUrlOrBase64 = base64, MimeType = mime };

    public static ContentPart ToolCall(string id, string name, string jsonArgs) =>
        new() { Kind = ContentKind.ToolCall, ToolCallId = id, Name = name, JsonPayload = jsonArgs };

    public static ContentPart ToolResult(string id, string jsonContent, string? text = null) =>
        new() { Kind = ContentKind.ToolResult, ToolCallId = id, JsonPayload = jsonContent, Text = text };
}

/// <summary>A multimodal chat message.</summary>
public sealed class ChatMessage
{
    public ChatRole Role { get; init; }

    public IReadOnlyList<ContentPart> Parts { get; init; } = Array.Empty<ContentPart>();

    /// <summary>Optional display name for the speaker.</summary>
    public string? Name { get; init; }

    /// <summary>Optional metadata bag (e.g. agent id, skill id).</summary>
    public IReadOnlyDictionary<string, string>? Meta { get; init; }

    public string AsPlainText() =>
        string.Concat(Parts.Where(p => p.Kind == ContentKind.Text).Select(p => p.Text));

    public static ChatMessage System(string text) => new() { Role = ChatRole.System, Parts = [ContentPart.FromText(text)] };
    public static ChatMessage User(string text) => new() { Role = ChatRole.User, Parts = [ContentPart.FromText(text)] };
    public static ChatMessage Assistant(string text) => new() { Role = ChatRole.Assistant, Parts = [ContentPart.FromText(text)] };
    public static ChatMessage Assistant(IReadOnlyList<ContentPart> parts) => new() { Role = ChatRole.Assistant, Parts = parts };

    public static ChatMessage Tool(string toolCallId, string jsonContent, string? text = null) =>
        new() { Role = ChatRole.Tool, Parts = [ContentPart.ToolResult(toolCallId, jsonContent, text)] };
}
