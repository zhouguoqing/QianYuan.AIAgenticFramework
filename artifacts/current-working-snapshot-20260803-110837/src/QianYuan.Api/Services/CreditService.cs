using Microsoft.EntityFrameworkCore;
using QianYuan.Api.Models;
using QianYuan.Data;
using QianYuan.Data.Entities;

namespace QianYuan.Api.Services;

public interface ICreditService
{
    Task EnsureDefaultPlansAsync(CancellationToken ct = default);
    Task<CreditWalletDto> EnsureWalletAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<CreditTransactionDto>> ListTransactionsAsync(Guid userId, int take, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default);
    EstimateCreditsResponse Estimate(EstimateCreditsRequest request);
    Task<CreditTransactionDto> ConsumeAsync(Guid userId, ConsumeCreditsRequest request, CancellationToken ct = default);
}

public sealed class CreditService : ICreditService
{
    private const string FreePlanId = "free";
    private readonly QianYuanDbContext _db;

    public CreditService(QianYuanDbContext db)
    {
        _db = db;
    }

    public async Task EnsureDefaultPlansAsync(CancellationToken ct = default)
    {
        if (await _db.SubscriptionPlans.AnyAsync(ct)) return;

        _db.SubscriptionPlans.AddRange(
            new SubscriptionPlan { Id = FreePlanId, Name = "体验版", MonthlyCredits = 500, MaxAssistants = 3, MaxProjects = 5, MaxAutoTasks = 3, AllowAllModels = false, PriceMonthlyCents = 0, SortOrder = 0 },
            new SubscriptionPlan { Id = "standard", Name = "标准版", MonthlyCredits = 4000, MaxAssistants = 5, MaxProjects = 10, MaxAutoTasks = 15, AllowAllModels = true, PriceMonthlyCents = 7000, SortOrder = 10 },
            new SubscriptionPlan { Id = "pro", Name = "高级版", MonthlyCredits = 9000, MaxAssistants = 8, MaxProjects = 15, MaxAutoTasks = 30, AllowAllModels = true, PriceMonthlyCents = 14000, SortOrder = 20 },
            new SubscriptionPlan { Id = "flagship", Name = "旗舰版", MonthlyCredits = 50000, MaxAssistants = 10, MaxProjects = 20, MaxAutoTasks = 99, AllowAllModels = true, PriceMonthlyCents = 70000, SortOrder = 30 });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<CreditWalletDto> EnsureWalletAsync(Guid userId, CancellationToken ct = default)
    {
        await EnsureDefaultPlansAsync(ct);
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        var subscription = await _db.UserSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == "Active", ct);

        if (subscription is null)
        {
            subscription = new UserSubscription
            {
                UserId = userId,
                PlanId = FreePlanId,
                Status = "Active",
                StartedAt = DateTime.UtcNow,
            };
            _db.UserSubscriptions.Add(subscription);
            await _db.SaveChangesAsync(ct);
            subscription = await _db.UserSubscriptions.Include(s => s.Plan).FirstAsync(s => s.Id == subscription.Id, ct);
        }

        var wallet = await _db.CreditWallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is null)
        {
            wallet = new CreditWallet
            {
                UserId = userId,
                Balance = subscription.Plan.MonthlyCredits,
                MonthlyQuota = subscription.Plan.MonthlyCredits,
                QuotaMonth = month,
                UpdatedAt = DateTime.UtcNow,
            };
            _db.CreditWallets.Add(wallet);
            _db.CreditTransactions.Add(new CreditTransaction
            {
                UserId = userId,
                Type = "Grant",
                Amount = subscription.Plan.MonthlyCredits,
                BalanceAfter = subscription.Plan.MonthlyCredits,
                SourceType = "Plan",
                SourceId = subscription.PlanId,
                Description = "Initial monthly credits",
                CreatedAt = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync(ct);
        }

        return ToWalletDto(wallet, subscription);
    }

    public async Task<IReadOnlyList<CreditTransactionDto>> ListTransactionsAsync(Guid userId, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);
        return await _db.CreditTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(take)
            .Select(t => new CreditTransactionDto(t.Id, t.Type, t.Amount, t.BalanceAfter, t.SourceType, t.SourceId, t.Description, t.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        await EnsureDefaultPlansAsync(ct);
        return await _db.SubscriptionPlans
            .AsNoTracking()
            .Where(p => p.Enabled)
            .OrderBy(p => p.SortOrder)
            .Select(p => new SubscriptionPlanDto(p.Id, p.Name, p.MonthlyCredits, p.MaxAssistants, p.MaxProjects, p.MaxAutoTasks, p.AllowAllModels, p.PriceMonthlyCents))
            .ToListAsync(ct);
    }

    public EstimateCreditsResponse Estimate(EstimateCreditsRequest request)
    {
        var inputCredits = Math.Ceiling(Math.Max(0, request.InputTokens) / 1000m);
        var outputCredits = Math.Ceiling(Math.Max(0, request.OutputTokens) / 1000m) * 3m;
        var multiplier = NormalizeTier(request.ModelTier) switch
        {
            "advanced" => 2m,
            "premium" => 5m,
            _ => 1m,
        };
        var taskFee = NormalizeTier(request.TaskType) switch
        {
            "deep-research" => 20m,
            "expert-team" => 10m,
            "image" => 30m,
            _ => 0m,
        };
        var total = (long)Math.Ceiling((inputCredits + outputCredits) * multiplier + taskFee);
        return new EstimateCreditsResponse(total, multiplier, "ceil((input/1000 + output/1000*3) * modelMultiplier + taskFee)");
    }

    public async Task<CreditTransactionDto> ConsumeAsync(Guid userId, ConsumeCreditsRequest request, CancellationToken ct = default)
    {
        if (request.Amount <= 0) throw new ArgumentException("Credit amount must be positive.");
        var walletDto = await EnsureWalletAsync(userId, ct);
        var wallet = await _db.CreditWallets.FirstAsync(w => w.UserId == userId, ct);
        if (wallet.Balance < request.Amount)
        {
            throw new InvalidOperationException($"Insufficient credits. Required {request.Amount}, available {walletDto.Balance}.");
        }

        wallet.Balance -= request.Amount;
        wallet.UpdatedAt = DateTime.UtcNow;
        var transaction = new CreditTransaction
        {
            UserId = userId,
            Type = "Consume",
            Amount = -request.Amount,
            BalanceAfter = wallet.Balance,
            SourceType = string.IsNullOrWhiteSpace(request.SourceType) ? "Task" : request.SourceType.Trim(),
            SourceId = string.IsNullOrWhiteSpace(request.SourceId) ? null : request.SourceId.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            CreatedAt = DateTime.UtcNow,
        };
        _db.CreditTransactions.Add(transaction);
        await _db.SaveChangesAsync(ct);
        return new CreditTransactionDto(transaction.Id, transaction.Type, transaction.Amount, transaction.BalanceAfter, transaction.SourceType, transaction.SourceId, transaction.Description, transaction.CreatedAt);
    }

    private static CreditWalletDto ToWalletDto(CreditWallet wallet, UserSubscription subscription)
    {
        return new CreditWalletDto(wallet.UserId, wallet.Balance, wallet.MonthlyQuota, wallet.QuotaMonth, subscription.PlanId, subscription.Plan.Name, wallet.UpdatedAt);
    }

    private static string NormalizeTier(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}