namespace QianYuan.Api.Models;

/// <summary>A localized expert category (e.g. 内容创作, 技术工程).</summary>
public sealed record ExpertCategoryDto(
    string Id,
    string Name,
    string Description,
    int Count);

/// <summary>Summary card for an expert or expert team in the marketplace grid.</summary>
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
    string? Author);

/// <summary>Full expert detail including prompts.</summary>
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
    string AgentName,
    string Plugin,
    string DefaultInitPrompt,
    IReadOnlyList<string> QuickPrompts);

/// <summary>A curated launch scenario grouping recommended experts.</summary>
public sealed record ExpertScenarioDto(
    string Id,
    string Name,
    string Description,
    string Accent,
    IReadOnlyList<ExpertSummaryDto> Experts);

/// <summary>Paged marketplace listing result.</summary>
public sealed record ExpertListResultDto(
    int Total,
    IReadOnlyList<ExpertSummaryDto> Items);
