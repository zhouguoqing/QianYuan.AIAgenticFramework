namespace QianYuan.Data.Entities;

public sealed class Conversation
{
    public string Id { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? Title { get; set; }
    public string? AgentId { get; set; }
    public string Status { get; set; } = "Active";
    public string? Summary { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ConversationMessage> Messages { get; set; } = [];
    public ICollection<ConversationTurn> Turns { get; set; } = [];
}

public sealed class ConversationMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ConversationId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string ContentJson { get; set; } = "{}";
    public int SortOrder { get; set; }
    public int? Tokens { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Conversation Conversation { get; set; } = null!;
}

public sealed class ConversationTurn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ConversationId { get; set; } = string.Empty;
    public Guid? UserMessageId { get; set; }
    public Guid? AssistantMessageId { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Conversation Conversation { get; set; } = null!;
}
