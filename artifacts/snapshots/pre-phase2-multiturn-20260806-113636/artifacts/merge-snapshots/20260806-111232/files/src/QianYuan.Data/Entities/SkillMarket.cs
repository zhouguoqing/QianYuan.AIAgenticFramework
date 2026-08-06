namespace QianYuan.Data.Entities;

public sealed class SkillPackage
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "general";
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SkillMarketEntry> Entries { get; set; } = [];
}

public sealed class SkillMarketEntry
{
    public string Id { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "general";
    public string TagsJson { get; set; } = "[]";
    public string TriggerPhrasesJson { get; set; } = "[]";
    public string SkillMarkdown { get; set; } = string.Empty;
    public string Source { get; set; } = "qianyuan-local";
    public string? SourceUrl { get; set; }
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public SkillPackage Package { get; set; } = null!;
}

public sealed class InstalledSkill
{
    public string SkillId { get; set; } = string.Empty;
    public string? MarketEntryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "general";
    public string TagsJson { get; set; } = "[]";
    public string TriggerPhrasesJson { get; set; } = "[]";
    public string Scope { get; set; } = "user";
    public string InstallPath { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
