namespace QianYuan.Data.Entities;

public sealed class CreditWallet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public long Balance { get; set; }
    public long MonthlyQuota { get; set; }
    public string QuotaMonth { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public UserAccount User { get; set; } = null!;
}

public sealed class CreditTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Type { get; set; } = "Consume";
    public long Amount { get; set; }
    public long BalanceAfter { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string? SourceId { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public UserAccount User { get; set; } = null!;
}

public sealed class SubscriptionPlan
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long MonthlyCredits { get; set; }
    public int MaxAssistants { get; set; }
    public int MaxProjects { get; set; }
    public int MaxAutoTasks { get; set; }
    public bool AllowAllModels { get; set; }
    public int PriceMonthlyCents { get; set; }
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class UserSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string PlanId { get; set; } = "free";
    public string Status { get; set; } = "Active";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }

    public UserAccount User { get; set; } = null!;
    public SubscriptionPlan Plan { get; set; } = null!;
}