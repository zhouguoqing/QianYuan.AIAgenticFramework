using QianYuan.Core.Models;

namespace QianYuan.Core.Abstractions;

/// <summary>
/// Dispatches tool calls coming from the LLM back into either skills or other agents.
/// Centralizes argument validation, timeouts, telemetry, and error wrapping.
/// </summary>
public interface IToolDispatcher
{
    /// <summary>Invoke a tool by its (fully-qualified) name.</summary>
    ValueTask<SkillInvocationResult> InvokeAsync(
        string toolName,
        string argumentsJson,
        SkillInvocationContext context,
        CancellationToken ct = default);
}
