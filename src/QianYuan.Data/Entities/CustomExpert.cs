namespace QianYuan.Data.Entities;

public sealed class CustomExpert
{
    public string Id { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string CategoryId { get; set; } = "custom";
    public string Name { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string TagsJson { get; set; } = "[]";
    public string QuickPromptsJson { get; set; } = "[]";
    public string? BoundAgentId { get; set; }
    public string? Author { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public UserAccount User { get; set; } = null!;
}
