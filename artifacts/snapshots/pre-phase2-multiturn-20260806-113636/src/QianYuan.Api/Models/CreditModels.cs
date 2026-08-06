namespace QianYuan.Api.Models;

public sealed record CreditWalletDto(
    Guid UserId,
    long Balance,
    long MonthlyQuota,
    string QuotaMonth,
    string PlanId,
    string PlanName,
    DateTime UpdatedAt);

public sealed record CreditTransactionDto(
    Guid Id,
    string Type,
    long Amount,
    long BalanceAfter,
    string SourceType,
    string? SourceId,
    string? Description,
    DateTime CreatedAt);

public sealed record SubscriptionPlanDto(
    string Id,
    string Name,
    long MonthlyCredits,
    int MaxAssistants,
    int MaxProjects,
    int MaxAutoTasks,
    bool AllowAllModels,
    int PriceMonthlyCents);

public sealed record EstimateCreditsRequest(
    long InputTokens,
    long OutputTokens,
    string? ModelTier,
    string? TaskType);

public sealed record EstimateCreditsResponse(
    long EstimatedCredits,
    decimal Multiplier,
    string Formula);

public sealed record ConsumeCreditsRequest(
    long Amount,
    string SourceType,
    string? SourceId,
    string? Description);