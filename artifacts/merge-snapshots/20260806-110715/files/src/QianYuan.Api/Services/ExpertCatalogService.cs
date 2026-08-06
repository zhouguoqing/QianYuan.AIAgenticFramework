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

public sealed class ExpertCatalogService : IExpertCatalogService
{
    private const string AssetBaseUrl =
        "https://acc-1258344699.cos.accelerate.myqcloud.com/workbuddy/expert-marketplace";

    private readonly Dictionary<string, ExpertCategoryDto> _categories = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ExpertCategoryDto> _categoryOrder = new();
    private readonly List<ExpertDetailDto> _experts = new();
    private readonly Dictionary<string, ExpertDetailDto> _expertById = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ExpertScenarioDto> _scenarios = new();
    private readonly Dictionary<string, string> _promptFileById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _promptCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _promptLock = new(1, 1);
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ExpertCatalogService> _logger;
    private readonly string _promptRoot;

    public ExpertCatalogService(IHostEnvironment env, IHttpClientFactory httpFactory, ILogger<ExpertCatalogService> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _promptRoot = Path.Combine(env.ContentRootPath, "Data", "experts", "prompts");

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
            logger.LogInformation("Loaded {Experts} experts across {Categories} categories.", _experts.Count, _categoryOrder.Count);
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
            items = items.Where(e => Matches(e, q));
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

            var persona = string.Empty;
            if (_promptFileById.TryGetValue(id, out var promptFile))
            {
                try
                {
                    var raw = await ReadPromptFileAsync(promptFile, ct).ConfigureAwait(false);
                    persona = StripFrontmatter(raw ?? string.Empty).Trim();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load expert persona for {Id} from {PromptFile}.", id, promptFile);
                }
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

    private async Task<string?> ReadPromptFileAsync(string promptFile, CancellationToken ct)
    {
        var localPath = BuildLocalPromptPath(promptFile);
        if (File.Exists(localPath))
            return await File.ReadAllTextAsync(localPath, ct).ConfigureAwait(false);

        var url = BuildAssetUrl(promptFile);
        if (url is null) return null;

        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        var raw = await client.GetStringAsync(url, ct).ConfigureAwait(false);

        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await File.WriteAllTextAsync(localPath, raw, ct).ConfigureAwait(false);
        return raw;
    }

    private string BuildLocalPromptPath(string promptFile)
    {
        var value = promptFile.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            value = uri.AbsolutePath;

        var segments = value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s != "." && s != "..")
            .Select(SafePathSegment)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

        return segments.Length == 0
            ? Path.Combine(_promptRoot, "unknown.md")
            : Path.Combine(new[] { _promptRoot }.Concat(segments).ToArray());
    }

    private static string? BuildAssetUrl(string promptFile)
    {
        var value = promptFile.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)) return uri.ToString();
        if (string.IsNullOrWhiteSpace(value)) return null;
        return AssetBaseUrl + (value.StartsWith('/') ? value : "/" + value);
    }

    private static string SafePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private static string BuildFallbackPersona(ExpertDetailDto e)
    {
        var tags = e.Tags.Count > 0 ? $"\n擅长领域：{string.Join("、", e.Tags)}。" : string.Empty;
        return $"你是{e.Name}，一位{e.Profession}。{e.Description}{tags}\n" +
               "请始终以该专家的身份、专业视角和语气回答用户问题，输出具体、可执行的建议。";
    }

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
            var dto = new ExpertCategoryDto(id, Localized(c, "name"), Localized(c, "description"), 0);
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
                false,
                GetString(e, "boundAgentId"),
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
        var configs = new (string Id, string Name, string Description, string Accent, string CategoryId, string[] Preferred)[]
        {
            ("content", "内容创作", "内容策略、多平台创作与品牌叙事。", "#7c6cff", "06-ContentCreative", new[] { "ContentCreator" }),
            ("investment", "投资分析", "交易分析、股票研究与投研决策。", "#0f9d76", "08-FinanceInvestment", new[] { "TradingAgentTeam", "StockPartnerTeam" }),
            ("legal", "法律咨询", "法律检索、合同审查与合规风控。", "#c8873a", "11-SecurityCompliance", Array.Empty<string>()),
            ("smb", "小微企业", "创业辅导、销售增长与运营提效。", "#3a7bd5", "12-IndustryConsultant", new[] { "SoftwareCompany" }),
            ("ecommerce", "电商运营", "电商运营、跨境增长与内容变现。", "#d5567b", "05-MarketingGrowth", Array.Empty<string>()),
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
            _scenarios.Add(new ExpertScenarioDto(cfg.Id, cfg.Name, cfg.Description, cfg.Accent, picks.Take(3).Select(ToSummary).ToList()));
        }
    }

    private static ExpertSummaryDto ToSummary(ExpertDetailDto e) => new(
        e.Id, e.CategoryId, e.CategoryName, e.Name, e.Profession, e.Description,
        e.AvatarUrl, e.Type, e.IsOpc, e.Tags, e.Author, e.IsCustom, e.BoundAgentId);

    private static bool Matches(ExpertDetailDto e, string q) =>
        e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
        e.Profession.Contains(q, StringComparison.OrdinalIgnoreCase) ||
        e.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
        (e.Author?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
        e.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase));

    private static string BuildAvatarUrl(string avatar)
    {
        if (string.IsNullOrEmpty(avatar)) return string.Empty;
        if (avatar.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return avatar;
        return AssetBaseUrl + (avatar.StartsWith('/') ? avatar : "/" + avatar);
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

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
