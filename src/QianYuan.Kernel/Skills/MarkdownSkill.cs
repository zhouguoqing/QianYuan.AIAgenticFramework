using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;

namespace QianYuan.Kernel.Skills;

/// <summary>
/// A prompt-only skill loaded from a Skill.md file. It contributes instructions to the agent when selected.
/// </summary>
public sealed class MarkdownSkill : ISkill
{
    public MarkdownSkill(
        string id,
        string name,
        string description,
        IReadOnlyList<string> tags,
        string systemPromptFragment,
        string sourcePath)
    {
        Id = id;
        Name = name;
        Description = description;
        Tags = tags;
        SystemPromptFragment = systemPromptFragment;
        SourcePath = sourcePath;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<string> Tags { get; }
    public string SystemPromptFragment { get; }
    public string SourcePath { get; }

    public ValueTask<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct = default)
        => ValueTask.FromResult<IReadOnlyList<ToolDefinition>>(Array.Empty<ToolDefinition>());

    public ValueTask<SkillInvocationResult> InvokeAsync(
        string toolName,
        string argumentsJson,
        SkillInvocationContext context,
        CancellationToken ct = default)
        => ValueTask.FromResult(SkillInvocationResult.Error($"Markdown skill '{Id}' does not expose invokable tools."));
}