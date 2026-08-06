namespace QianYuan.Core.Exceptions;

public class QianYuanException : Exception
{
    public QianYuanException(string message) : base(message) { }
    public QianYuanException(string message, Exception inner) : base(message, inner) { }
}

public sealed class LlmProviderException : QianYuanException
{
    public string ProviderId { get; }
    public int? HttpStatus { get; }

    public LlmProviderException(string providerId, string message, int? httpStatus = null, Exception? inner = null)
        : base($"[{providerId}] {message}", inner ?? new InvalidOperationException(message))
    {
        ProviderId = providerId;
        HttpStatus = httpStatus;
    }
}

public sealed class SkillNotFoundException : QianYuanException
{
    public string SkillId { get; }
    public SkillNotFoundException(string skillId) : base($"Skill not found: {skillId}") => SkillId = skillId;
}

public sealed class ToolNotFoundException : QianYuanException
{
    public string ToolName { get; }
    public ToolNotFoundException(string toolName) : base($"Tool not found: {toolName}") => ToolName = toolName;
}

public sealed class AgentNotFoundException : QianYuanException
{
    public string AgentId { get; }
    public AgentNotFoundException(string agentId) : base($"Agent not found: {agentId}") => AgentId = agentId;
}

public sealed class ReActIterationLimitException : QianYuanException
{
    public int Limit { get; }
    public ReActIterationLimitException(int limit) : base($"ReAct iteration limit ({limit}) exceeded.") => Limit = limit;
}
