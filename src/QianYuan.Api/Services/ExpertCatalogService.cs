using System.Text.Json;
using QianYuan.Api.Models;

namespace QianYuan.Api.Services;

public interface IExpertCatalogService
{
    IReadOnlyList<ExpertCategoryDto> ListCategories();
    ExpertListResultDto ListExperts(string? categoryId, string? type, string? query, string? sort);
    ExpertDetailDto? GetExpert(string id);
    IReadOnlyList<ExpertScenarioDto> ListScenarios();
    Task<string?> GetPromptAsync(string id, CancellationToken ct);
}

/// <summary>
/// Serves the WorkBuddy-style expert marketplace from a bundled manifest.
/// Data is loaded once at startup and cached in memory (Chinese text preferred).
/// </summary>
public sealed class ExpertCatalogService : IExpertCatalogService
{
    private const string AvatarBaseUrl =
        "https://acc-1258344699.cos.accelerate.myqcloud.com/workbuddy/expert-marketplace";

    private readonly Dictionary<string, ExpertCategoryDto> _categories = new();
    private readonly List<ExpertCategoryDto> _categoryOrder = new();
    private readonly List<ExpertDetailDto> _experts = new();
    private readonly Dictionary<string, ExpertDetailDto> _expertById = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ExpertScenarioDto> _scenarios = new();
    private readonly Dictionary<string, string> _promptFileById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _promptCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _promptLock = new(1, 1);
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ExpertCatalogService> _logger;

    public ExpertCatalogService(IHostEnvironment env, IHttpClientFactory httpFactory, ILogger<ExpertCatalogService> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        var path = Path.Combine(env.ContentRootPath, "Data", "experts", "expert-manifest.json");
        if (!File.Exists(path))
        {
            logger.LogWarning("Expert manifest not found at {Path}; marketplace will be empty.", path);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            LoadCategories(root);
            LoadExperts(root);
            RecountCategories();
            BuildScenarios();
            logger.LogInformation(
                "Loaded {Experts} experts across {Categories} categories.",
                _experts.Count, _categoryOrder.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse expert manifest at {Path}.", path);
        }
    }

    public IReadOnlyList<ExpertCategoryDto> ListCategories() => _categoryOrder;

    public ExpertListResultDto ListExperts(string? categoryId, string? type, string? query, string? sort)
    {
        IEnumerable<ExpertDetailDto> items = _experts;

        if (!string.IsNullOrWhiteSpace(categoryId) && !categoryId.Equals("all", StringComparison.OrdinalIgnoreCase))
            items = items.Where(e => e.CategoryId.Equals(categoryId, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(type) && !type.Equals("all", StringComparison.OrdinalIgnoreCase))
            items = items.Where(e => e.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            items = items.Where(e =>
                e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Profession.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        var list = items.ToList();
        if (string.Equals(sort, "newest", StringComparison.OrdinalIgnoreCase))
            list.Reverse();

        var summaries = list.Select(ToSummary).ToList();
        return new ExpertListResultDto(summaries.Count, summaries);
    }

    public ExpertDetailDto? GetExpert(string id) =>
        _expertById.TryGetValue(id, out var expert) ? expert : null;

    public IReadOnlyList<ExpertScenarioDto> ListScenarios() => _scenarios;

    public async Task<string?> GetPromptAsync(string id, CancellationToken ct)
    {
        if (!_expertById.TryGetValue(id, out var expert)) return null;
        if (_promptCache.TryGetValue(id, out var cached)) return cached;

        await _promptLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_promptCache.TryGetValue(id, out cached)) return cached;

            string persona;
            if (_promptFileById.TryGetValue(id, out var promptFile))
            {
                var url = AvatarBaseUrl + (promptFile.StartsWith('/') ? promptFile : "/" + promptFile);
                try
                {
                    var client = _httpFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var raw = await client.GetStringAsync(url, ct).ConfigureAwait(false);
                    persona = StripFrontmatter(raw).Trim();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch expert persona for {Id} from {Url}.", id, url);
                    persona = BuildFallbackPersona(expert);
                }
            }
            else
            {
                persona = BuildFallbackPersona(expert);
            }

            if (string.IsNullOrWhiteSpace(persona))
                persona = BuildFallbackPersona(expert);

            _promptCache[id] = persona;
            return persona;
        }
        finally
        {
            _promptLock.Release();
        }
    }

    private static string BuildFallbackPersona(ExpertDetailDto e)
    {
        var tags = e.Tags.Count > 0 ? $"\n擅长领域：{string.Join("、", e.Tags)}。" : string.Empty;
        return $"你是{e.Name}，一位{e.Profession}。{e.Description}{tags}\n" +
               "请始终以该专家的身份、专业视角和语气回答用户的问题，输出具体、可执行的建议。";
    }

    /// <summary>Removes a leading YAML frontmatter block (--- ... ---) if present.</summary>
    private static string StripFrontmatter(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        var text = content.TrimStart('\uFEFF', ' ', '\r', '\n');
        if (!text.StartsWith("---")) return content;
        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return content;
        var after = text.IndexOf('\n', end + 1);
        return after < 0 ? string.Empty : text[(after + 1)..];
    }

    private void LoadCategories(JsonElement root)
    {
        if (!root.TryGetProperty("categories", out var cats) || cats.ValueKind != JsonValueKind.Array)
            return;

        foreach (var c in cats.EnumerateArray())
        {
            var id = GetString(c, "id");
            if (string.IsNullOrEmpty(id)) continue;
            var dto = new ExpertCategoryDto(
                id,
                Localized(c, "name"),
                Localized(c, "description"),
                0);
            _categories[id] = dto;
            _categoryOrder.Add(dto);
        }
    }

    private void LoadExperts(JsonElement root)
    {
        if (!root.TryGetProperty("experts", out var experts) || experts.ValueKind != JsonValueKind.Array)
            return;

        foreach (var e in experts.EnumerateArray())
        {
            var id = GetString(e, "id");
            if (string.IsNullOrEmpty(id)) continue;

            var categoryId = GetString(e, "categoryId") ?? string.Empty;
            var categoryName = _categories.TryGetValue(categoryId, out var cat) ? cat.Name : categoryId;
            var avatar = GetString(e, "avatar") ?? string.Empty;
            var detail = new ExpertDetailDto(
                id,
                categoryId,
                categoryName,
                Localized(e, "displayName"),
                Localized(e, "profession"),
                Localized(e, "description"),
                BuildAvatarUrl(avatar),
                GetString(e, "expertType") ?? "agent",
                e.TryGetProperty("isOPC", out var opc) && opc.ValueKind == JsonValueKind.True,
                LocalizedList(e, "tags"),
                e.TryGetProperty("author", out _) ? Localized(e, "author") : null,
                GetString(e, "agentName") ?? string.Empty,
                GetString(e, "plugin") ?? string.Empty,
                Localized(e, "defaultInitPrompt"),
                LocalizedList(e, "quickPrompts"));

            _experts.Add(detail);
            _expertById[id] = detail;

            var promptFile = GetString(e, "promptFile");
            if (!string.IsNullOrWhiteSpace(promptFile))
                _promptFileById[id] = promptFile;
        }
    }

    private void RecountCategories()
    {
        for (var i = 0; i < _categoryOrder.Count; i++)
        {
            var cat = _categoryOrder[i];
            var count = _experts.Count(e => e.CategoryId == cat.Id);
            var updated = cat with { Count = count };
            _categoryOrder[i] = updated;
            _categories[cat.Id] = updated;
        }
    }

    private void BuildScenarios()
    {
        // Curated launch scenarios (精选场景) mapped to categories, WorkBuddy-style.
        var configs = new (string Id, string Name, string Description, string Accent, string CategoryId, string[] Preferred)[]
        {
            ("content", "内容创作", "内容策略、多平台创作与品牌叙事，一站式产出优质内容。", "#7c6cff", "06-ContentCreative",
                new[] { "ContentCreator" }),
            ("investment", "投资分析", "交易分析、股票研究与投研决策，辅助你的投资判断。", "#0f9d76", "08-FinanceInvestment",
                new[] { "TradingAgentTeam", "StockPartnerTeam" }),
            ("legal", "法律咨询", "法律检索、合同审查与合规风控，专业法务支持。", "#c8873a", "11-SecurityCompliance",
                Array.Empty<string>()),
            ("smb", "小微企业", "创业辅导、销售增长与运营提效，助力小微企业成长。", "#3a7bd5", "12-IndustryConsultant",
                new[] { "SoftwareCompany" }),
            ("ecommerce", "电商运营", "电商运营、跨境增长与内容变现，打造增长闭环。", "#d5567b", "05-MarketingGrowth",
                Array.Empty<string>()),
        };

        foreach (var cfg in configs)
        {
            var picks = new List<ExpertDetailDto>();
            foreach (var pid in cfg.Preferred)
            {
                if (_expertById.TryGetValue(pid, out var e) && !picks.Contains(e))
                    picks.Add(e);
            }

            foreach (var e in _experts.Where(x => x.CategoryId == cfg.CategoryId))
            {
                if (picks.Count >= 3) break;
                if (!picks.Contains(e)) picks.Add(e);
            }

            if (picks.Count == 0) continue;
            _scenarios.Add(new ExpertScenarioDto(
                cfg.Id,
                cfg.Name,
                cfg.Description,
                cfg.Accent,
                picks.Take(3).Select(ToSummary).ToList()));
        }
    }

    private static ExpertSummaryDto ToSummary(ExpertDetailDto e) => new(
        e.Id, e.CategoryId, e.CategoryName, e.Name, e.Profession, e.Description,
        e.AvatarUrl, e.Type, e.IsOpc, e.Tags, e.Author);

    private static string BuildAvatarUrl(string avatar)
    {
        if (string.IsNullOrEmpty(avatar)) return string.Empty;
        if (avatar.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return avatar;
        return AvatarBaseUrl + (avatar.StartsWith('/') ? avatar : "/" + avatar);
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Reads a localized object, preferring Chinese then English.</summary>
    private static string Localized(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return string.Empty;
        if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? string.Empty;
        if (v.ValueKind != JsonValueKind.Object) return string.Empty;
        if (v.TryGetProperty("zh", out var zh) && zh.ValueKind == JsonValueKind.String)
            return zh.GetString() ?? string.Empty;
        if (v.TryGetProperty("en", out var en) && en.ValueKind == JsonValueKind.String)
            return en.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static IReadOnlyList<string> LocalizedList(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var result = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                var zh = item.TryGetProperty("zh", out var z) && z.ValueKind == JsonValueKind.String ? z.GetString() : null;
                var en = item.TryGetProperty("en", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                var val = zh ?? en;
                if (!string.IsNullOrWhiteSpace(val)) result.Add(val);
            }
        }
        return result;
    }
}
