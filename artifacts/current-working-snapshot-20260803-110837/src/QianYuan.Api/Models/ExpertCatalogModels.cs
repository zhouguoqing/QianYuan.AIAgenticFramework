namespace QianYuan.Api.Models;

public sealed record ExpertCategoryDto(
    string Id,
    string Name,
    string Description,
    int Count);

public sealed record ExpertSummaryDto(
    string Id,
    string CategoryId,
    string CategoryName,
    string Name,
    string Profession,
    string Description,
    string AvatarUrl,
    string Type,
    bool IsOpc,
    IReadOnlyList<string> Tags,
    string? Author,
    bool IsCustom,
    string? BoundAgentId);

public sealed record ExpertDetailDto(
    string Id,
    string CategoryId,
    string CategoryName,
    string Name,
    string Profession,
    string Description,
    string AvatarUrl,
    string Type,
    bool IsOpc,
    IReadOnlyList<string> Tags,
    string? Author,
    bool IsCustom,
    string? BoundAgentId,
    string AgentName,
    string Plugin,
    string DefaultInitPrompt,
    IReadOnlyList<string> QuickPrompts);

public sealed record ExpertScenarioDto(
    string Id,
    string Name,
    string Description,
    string Accent,
    IReadOnlyList<ExpertSummaryDto> Experts);

public sealed record ExpertListResultDto(
    int Total,
    IReadOnlyList<ExpertSummaryDto> Items);

public sealed record CustomExpertUpsertRequest(
    string? Id,
    string Name,
    string Profession,
    string Description,
    string SystemPrompt,
    string? CategoryId,
    string? AvatarUrl,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<string>? QuickPrompts,
    string? BoundAgentId,
    string? Author);

public sealed record ExpertBindAgentRequest(string? BoundAgentId);

public sealed record ExpertPromptDto(string Id, string SystemPrompt, string? BoundAgentId);

public sealed record ExpertChatRequest(string Message, string? QuickPrompt, string? Provider, string? Model);

public sealed record ExpertChatResponse(
    string ExpertId,
    string? BoundAgentId,
    string Content,
    IReadOnlyList<string> Chunks);
