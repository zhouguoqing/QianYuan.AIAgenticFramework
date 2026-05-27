namespace QianYuan.Core.Models;

/// <summary>Declaration of a tool/function the LLM may call.</summary>
public sealed class ToolDefinition
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    /// <summary>JSON Schema describing arguments (draft 2020-12 compatible).</summary>
    public required string JsonSchema { get; init; }

    /// <summary>Optional tag used by progressive loading to group tools by skill.</summary>
    public string? SkillId { get; init; }
}

/// <summary>Sampling and behavior knobs sent to the provider.</summary>
public sealed class GenerationOptions
{
    public string? Model { get; init; }
    public float? Temperature { get; init; }
    public float? TopP { get; init; }
    public int? MaxOutputTokens { get; init; }
    public IReadOnlyList<string>? StopSequences { get; init; }

    /// <summary>If true, request the provider stream chunks.</summary>
    public bool Stream { get; init; } = true;

    /// <summary>"auto", "none", "required" or a specific tool name.</summary>
    public string? ToolChoice { get; init; }

    /// <summary>Enable provider-side prompt caching when supported (Claude).</summary>
    public bool EnablePromptCaching { get; init; } = true;

    /// <summary>Optional vendor-specific extensions (e.g. thinking budget).</summary>
    public IReadOnlyDictionary<string, object?>? Extensions { get; init; }
}

/// <summary>A single completion request.</summary>
public sealed class ChatRequest
{
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public IReadOnlyList<ToolDefinition>? Tools { get; init; }
    public GenerationOptions Options { get; init; } = new();
}

/// <summary>Token usage report.</summary>
public sealed record TokenUsage(int InputTokens, int OutputTokens, int? CacheReadTokens = null, int? CacheWriteTokens = null)
{
    public int TotalTokens => InputTokens + OutputTokens;
}

/// <summary>A non-streaming completion result.</summary>
public sealed class ChatResponse
{
    public required ChatMessage Message { get; init; }
    public string? FinishReason { get; init; }
    public TokenUsage? Usage { get; init; }
    public string? Model { get; init; }
}
