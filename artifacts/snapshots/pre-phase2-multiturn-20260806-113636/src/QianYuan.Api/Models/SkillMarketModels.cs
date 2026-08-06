namespace QianYuan.Api.Models;

public sealed record SkillCategoryDto(string Id, string Name, int MarketCount, int InstalledCount);

public sealed record SkillPackageDto(
    string Id,
    string Name,
    string Description,
    string Category,
    int SortOrder,
    IReadOnlyList<SkillMarketEntryDto> Entries);

public sealed record SkillMarketEntryDto(
    string Id,
    string PackageId,
    string PackageName,
    string Name,
    string Description,
    string Category,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> TriggerPhrases,
    string Source,
    string? SourceUrl,
    bool Installed,
    string? InstalledSkillId,
    bool Enabled);

public sealed record InstalledSkillDto(
    string SkillId,
    string? MarketEntryId,
    string Name,
    string Description,
    string Category,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> TriggerPhrases,
    string Scope,
    string InstallPath,
    bool Enabled,
    DateTime InstalledAt,
    DateTime UpdatedAt);

public sealed record InstallSkillRequest(string MarketEntryId, bool Enabled = true);

public sealed record CreateSkillRequest(
    string Id,
    string Name,
    string Description,
    string Body,
    string? Category,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<string>? TriggerPhrases,
    string? Scope);

public sealed record SetSkillEnabledRequest(bool Enabled);
