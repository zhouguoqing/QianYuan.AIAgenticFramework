using QianYuan.Core.Models;
using QianYuan.Core.Streaming;

namespace QianYuan.Core.Abstractions;

/// <summary>
/// Abstraction over a chat-style large language model provider.
/// Implementations: OpenAI-compat, Anthropic, Gemini, Qwen DashScope, etc.
/// </summary>
public interface ILlmProvider
{
    /// <summary>Stable provider id, e.g. "openai-compat", "claude", "gemini", "qwen".</summary>
    string ProviderId { get; }

    /// <summary>Default model id when none supplied in <see cref="GenerationOptions"/>.</summary>
    string DefaultModel { get; }

    /// <summary>Capabilities advertised by this provider (used by Kernel to pick).</summary>
    LlmCapabilities Capabilities { get; }

    /// <summary>Non-streaming completion. Implementations may simply collect a stream internally.</summary>
    Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default);

    /// <summary>Streaming completion. Yields chunks until <see cref="StreamingChunkKind.End"/>.</summary>
    IAsyncEnumerable<StreamingChunk> StreamAsync(ChatRequest request, CancellationToken ct = default);
}

/// <summary>Capability flags reported by a provider.</summary>
[Flags]
public enum LlmCapabilities
{
    None = 0,
    Streaming = 1 << 0,
    Tools = 1 << 1,
    Vision = 1 << 2,
    Audio = 1 << 3,
    PromptCaching = 1 << 4,
    Thinking = 1 << 5,
    JsonMode = 1 << 6,
    ParallelToolCalls = 1 << 7
}
