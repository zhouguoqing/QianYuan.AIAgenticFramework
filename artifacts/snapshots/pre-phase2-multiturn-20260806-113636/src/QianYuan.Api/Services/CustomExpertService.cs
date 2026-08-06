using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QianYuan.Api.Models;
using QianYuan.Data;
using QianYuan.Data.Entities;

namespace QianYuan.Api.Services;

public interface ICustomExpertService
{
    Task<IReadOnlyList<ExpertSummaryDto>> ListAsync(Guid? userId, string? categoryId, string? type, string? query, string? sort, string? tag, string? author, CancellationToken ct);
    Task<ExpertDetailDto?> GetAsync(Guid? userId, string id, CancellationToken ct);
    Task<string?> GetPromptAsync(Guid? userId, string id, CancellationToken ct);
    Task<ExpertDetailDto> CreateAsync(Guid userId, CustomExpertUpsertRequest request, CancellationToken ct);
    Task<ExpertDetailDto> UpdateAsync(Guid userId, string id, CustomExpertUpsertRequest request, CancellationToken ct);
    Task<ExpertDetailDto> BindAgentAsync(Guid userId, string id, string? boundAgentId, CancellationToken ct);
    Task DeleteAsync(Guid userId, string id, CancellationToken ct);
}

public sealed class CustomExpertService : ICustomExpertService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly QianYuanDbContext _db;
    private readonly IExpertCatalogService _catalog;

    public CustomExpertService(QianYuanDbContext db, IExpertCatalogService catalog)
    {
        _db = db;
        _catalog = catalog;
    }

    public async Task<IReadOnlyList<ExpertSummaryDto>> ListAsync(
        Guid? userId,
        string? categoryId,
        string? type,
        string? query,
        string? sort,
        string? tag,
        string? author,
        CancellationToken ct)
    {
        if (userId is null) return Array.Empty<ExpertSummaryDto>();

        var rows = await _db.CustomExperts.AsNoTracking()
            .Where(e => e.UserId == userId.Value && e.Enabled)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        IEnumerable<ExpertDetailDto> items = rows.Select(ToDetail);

        if (!string.IsNullOrWhiteSpace(categoryId) && !categoryId.Equals("all", StringComparison.OrdinalIgnoreCase))
            items = items.Where(e => e.CategoryId.Equals(categoryId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(type) && !type.Equals("all", StringComparison.OrdinalIgnoreCase))
            items = items.Where(e => e.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            items = items.Where(e => Matches(e, q));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var value = tag.Trim();
            items = items.Where(e => e.Tags.Any(t => t.Equals(value, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(author))
        {
            var value = author.Trim();
            items = items.Where(e => e.Author?.Contains(value, StringComparison.OrdinalIgnoreCase) == true);
        }

        var list = items.ToList();
        if (string.Equals(sort, "newest", StringComparison.OrdinalIgnoreCase))
            list = list.OrderByDescending(e => e.Id).ToList();

        return list.Select(ToSummary).ToList();
    }

    public async Task<ExpertDetailDto?> GetAsync(Guid? userId, string id, CancellationToken ct)
    {
        if (userId is null) return null;
        var row = await _db.CustomExperts.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId.Value && e.Enabled, ct)
            .ConfigureAwait(false);
        return row is null ? null : ToDetail(row);
    }

    public async Task<string?> GetPromptAsync(Guid? userId, string id, CancellationToken ct)
    {
        if (userId is null) return null;
        var row = await _db.CustomExperts.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId.Value && e.Enabled, ct)
            .ConfigureAwait(false);
        return row is null ? null : BuildPrompt(row);
    }

    public async Task<ExpertDetailDto> CreateAsync(Guid userId, CustomExpertUpsertRequest request, CancellationToken ct)
    {
        Validate(request);
        var id = NormalizeId(request.Id ?? request.Name);
        if (_catalog.GetExpert(id) is not null || await _db.CustomExperts.AnyAsync(e => e.Id == id, ct).ConfigureAwait(false))
            id = $"{id}-{Guid.NewGuid():N}"[..Math.Min(id.Length + 9, 256)];

        var now = DateTime.UtcNow;
        var row = new CustomExpert
        {
            Id = id,
            UserId = userId,
            CategoryId = Normalize(request.CategoryId, "custom"),
            Name = request.Name.Trim(),
            Profession = request.Profession.Trim(),
            Description = request.Description.Trim(),
            AvatarUrl = request.AvatarUrl?.Trim() ?? string.Empty,
            SystemPrompt = request.SystemPrompt.Trim(),
            TagsJson = SerializeList(request.Tags, 8),
            QuickPromptsJson = SerializeList(request.QuickPrompts, 6),
            BoundAgentId = NormalizeNullable(request.BoundAgentId),
            Author = NormalizeNullable(request.Author),
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.CustomExperts.Add(row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDetail(row);
    }

    public async Task<ExpertDetailDto> UpdateAsync(Guid userId, string id, CustomExpertUpsertRequest request, CancellationToken ct)
    {
        Validate(request);
        var row = await _db.CustomExperts.FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId && e.Enabled, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Custom expert '{id}' not found.");

        row.CategoryId = Normalize(request.CategoryId, "custom");
        row.Name = request.Name.Trim();
        row.Profession = request.Profession.Trim();
        row.Description = request.Description.Trim();
        row.AvatarUrl = request.AvatarUrl?.Trim() ?? string.Empty;
        row.SystemPrompt = request.SystemPrompt.Trim();
        row.TagsJson = SerializeList(request.Tags, 8);
        row.QuickPromptsJson = SerializeList(request.QuickPrompts, 6);
        row.BoundAgentId = NormalizeNullable(request.BoundAgentId);
        row.Author = NormalizeNullable(request.Author);
        row.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDetail(row);
    }

    public async Task<ExpertDetailDto> BindAgentAsync(Guid userId, string id, string? boundAgentId, CancellationToken ct)
    {
        var row = await _db.CustomExperts.FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId && e.Enabled, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Custom expert '{id}' not found.");

        row.BoundAgentId = NormalizeNullable(boundAgentId);
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDetail(row);
    }

    public async Task DeleteAsync(Guid userId, string id, CancellationToken ct)
    {
        var row = await _db.CustomExperts.FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId && e.Enabled, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Custom expert '{id}' not found.");

        row.Enabled = false;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private ExpertDetailDto ToDetail(CustomExpert row)
    {
        var categoryName = _catalog.ListCategories().FirstOrDefault(c => c.Id.Equals(row.CategoryId, StringComparison.OrdinalIgnoreCase))?.Name
            ?? (row.CategoryId.Equals("custom", StringComparison.OrdinalIgnoreCase) ? "自定义专家" : row.CategoryId);
        var quickPrompts = DeserializeList(row.QuickPromptsJson);
        return new ExpertDetailDto(
            row.Id,
            row.CategoryId,
            categoryName,
            row.Name,
            row.Profession,
            row.Description,
            row.AvatarUrl,
            "agent",
            false,
            DeserializeList(row.TagsJson),
            row.Author,
            true,
            row.BoundAgentId,
            row.BoundAgentId ?? string.Empty,
            "custom",
            quickPrompts.FirstOrDefault() ?? $"请以{row.Name}的身份协助我完成这项任务。",
            quickPrompts);
    }

    private static ExpertSummaryDto ToSummary(ExpertDetailDto e) => new(
        e.Id, e.CategoryId, e.CategoryName, e.Name, e.Profession, e.Description,
        e.AvatarUrl, e.Type, e.IsOpc, e.Tags, e.Author, e.IsCustom, e.BoundAgentId);

    private static string BuildPrompt(CustomExpert row)
    {
        if (!string.IsNullOrWhiteSpace(row.SystemPrompt)) return row.SystemPrompt;
        var tags = DeserializeList(row.TagsJson);
        var tagText = tags.Count > 0 ? $"\n擅长领域：{string.Join("、", tags)}。" : string.Empty;
        return $"你是{row.Name}，一位{row.Profession}。{row.Description}{tagText}\n请给出具体、可执行的建议。";
    }

    private static bool Matches(ExpertDetailDto e, string q) =>
        e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
        e.Profession.Contains(q, StringComparison.OrdinalIgnoreCase) ||
        e.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
        (e.Author?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
        e.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase));

    private static void Validate(CustomExpertUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("专家名称不能为空。");
        if (string.IsNullOrWhiteSpace(request.Profession)) throw new ArgumentException("专家职业不能为空。");
        if (string.IsNullOrWhiteSpace(request.Description)) throw new ArgumentException("专家描述不能为空。");
        if (string.IsNullOrWhiteSpace(request.SystemPrompt)) throw new ArgumentException("系统提示词不能为空。");
    }

    private static string NormalizeId(string value)
    {
        var slug = new string(value.Trim().ToLowerInvariant().Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray());
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        if (string.IsNullOrWhiteSpace(slug)) slug = Guid.NewGuid().ToString("N")[..8];
        var normalized = slug.StartsWith("custom-", StringComparison.OrdinalIgnoreCase) ? slug : $"custom-{slug}";
        return normalized.Length <= 256 ? normalized : normalized[..256];
    }

    private static string SerializeList(IReadOnlyList<string>? values, int max) =>
        JsonSerializer.Serialize((values ?? Array.Empty<string>())
            .Select(v => v.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToArray(), JsonOptions);

    private static IReadOnlyList<string> DeserializeList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string Normalize(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string? NormalizeNullable(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
