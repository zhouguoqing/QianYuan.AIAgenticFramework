namespace QianYuan.Core.Streaming;

/// <summary>Kind of streamed chunk emitted while generating a response.</summary>
public enum StreamingChunkKind
{
    /// <summary>Marks the start of generation. May carry the model id.</summary>
    Start,

    /// <summary>A piece of assistant text. Concatenate Text.</summary>
    TextDelta,

    /// <summary>Provider-side "thinking" / reasoning trace (Claude extended thinking, Gemini thoughts).</summary>
    ThinkingDelta,

    /// <summary>Announces a tool call. ToolCallId + ToolName are set.</summary>
    ToolCallStart,

    /// <summary>Streamed JSON-args delta for the most recent tool call.</summary>
    ToolCallArgsDelta,

    /// <summary>Tool call fully assembled and ready for dispatch.</summary>
    ToolCallEnd,

    /// <summary>ReAct / agent loop observation that was fed back to the model.</summary>
    ToolObservation,

    /// <summary>Token-usage report mid-stream (most providers report at the end).</summary>
    Usage,

    /// <summary>Generation completed. FinishReason may be set.</summary>
    End,

    /// <summary>A recoverable warning surfaced from the provider.</summary>
    Warning,

    /// <summary>A terminal error. Concatenate Text for the message.</summary>
    Error
}

/// <summary>One streamed chunk from a provider or agent run.</summary>
public sealed record StreamingChunk
{
    public StreamingChunkKind Kind { get; init; }
    public string? Text { get; init; }
    public string? ToolCallId { get; init; }
    public string? ToolName { get; init; }
    public string? ToolArgsJson { get; init; }
    public string? FinishReason { get; init; }
    public string? Model { get; init; }
    public Models.TokenUsage? Usage { get; init; }

    /// <summary>Optional agent / skill / step identifiers for UI rendering.</summary>
    public string? AgentId { get; init; }
    public string? SkillId { get; init; }
    public int? Step { get; init; }

    public static StreamingChunk OfText(string text, string? agentId = null) =>
        new() { Kind = StreamingChunkKind.TextDelta, Text = text, AgentId = agentId };

    public static StreamingChunk Thinking(string text, string? agentId = null) =>
        new() { Kind = StreamingChunkKind.ThinkingDelta, Text = text, AgentId = agentId };

    public static StreamingChunk Observation(string text, string? toolCallId = null) =>
        new() { Kind = StreamingChunkKind.ToolObservation, Text = text, ToolCallId = toolCallId };

    public static StreamingChunk Start(string? model = null) =>
        new() { Kind = StreamingChunkKind.Start, Model = model };

    public static StreamingChunk End(string? finishReason = null, Models.TokenUsage? usage = null) =>
        new() { Kind = StreamingChunkKind.End, FinishReason = finishReason, Usage = usage };

    public static StreamingChunk Error(string message) =>
        new() { Kind = StreamingChunkKind.Error, Text = message };

    public static StreamingChunk Warning(string message) =>
        new() { Kind = StreamingChunkKind.Warning, Text = message };
}
