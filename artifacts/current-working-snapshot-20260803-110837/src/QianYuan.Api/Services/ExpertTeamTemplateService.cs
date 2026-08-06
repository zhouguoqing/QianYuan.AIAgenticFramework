using System.Text.Json;
using QianYuan.Api.Models;

namespace QianYuan.Api.Services;

public interface IExpertTeamTemplateService
{
    IReadOnlyList<ExpertTeamTemplateDto> ListTemplates();
    ExpertTeamTemplateDto? GetTemplate(string id);
}

public sealed class ExpertTeamTemplateService : IExpertTeamTemplateService
{
    private readonly List<ExpertTeamTemplateDto> _templates = new();
    private readonly Dictionary<string, ExpertTeamTemplateDto> _templateById = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ExpertTeamTemplateService> _logger;

    public ExpertTeamTemplateService(IHostEnvironment env, ILogger<ExpertTeamTemplateService> logger)
    {
        _logger = logger;
        var path = Path.Combine(env.ContentRootPath, "Data", "experts", "expert-manifest.json");
        if (!File.Exists(path))
        {
            _logger.LogWarning("Expert team manifest not found at {Path}.", path);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("experts", out var experts) || experts.ValueKind != JsonValueKind.Array)
                return;

            foreach (var item in experts.EnumerateArray())
            {
                if (!string.Equals(GetString(item, "expertType"), "team", StringComparison.OrdinalIgnoreCase)) continue;
                var template = ToTemplate(item);
                if (template is null) continue;
                _templates.Add(template);
                _templateById[template.Id] = template;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load expert team templates from {Path}.", path);
        }
    }

    public IReadOnlyList<ExpertTeamTemplateDto> ListTemplates() => _templates;

    public ExpertTeamTemplateDto? GetTemplate(string id) =>
        _templateById.TryGetValue(id, out var template) ? template : null;

    private static ExpertTeamTemplateDto? ToTemplate(JsonElement item)
    {
        var id = GetString(item, "id");
        if (string.IsNullOrWhiteSpace(id)) return null;

        var members = new List<ExpertTeamTemplateMemberDto>();
        if (item.TryGetProperty("members", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var order = 0;
            foreach (var member in arr.EnumerateArray())
            {
                order++;
                var role = GetString(member, "role") ?? (order == 1 ? "lead" : "member");
                var memberId = GetString(member, "id") ?? $"member-{order}";
                var name = Localized(member, "displayName");
                var profession = Localized(member, "profession");
                var isLead = role.Equals("lead", StringComparison.OrdinalIgnoreCase) || order == 1;
                members.Add(new ExpertTeamTemplateMemberDto(
                    memberId,
                    string.IsNullOrWhiteSpace(name) ? memberId : name,
                    profession,
                    BuildResponsibility(profession, isLead),
                    isLead ? "Sequential" : "Parallel"));
            }
        }

        if (members.Count == 0)
        {
            var agentName = GetString(item, "agentName") ?? id;
            members.Add(new ExpertTeamTemplateMemberDto(
                agentName,
                Localized(item, "displayName"),
                Localized(item, "profession"),
                Localized(item, "description"),
                "Sequential"));
        }

        return new ExpertTeamTemplateDto(
            id,
            Localized(item, "displayName"),
            Localized(item, "description"),
            GetString(item, "plugin") ?? id,
            GetString(item, "categoryId") ?? string.Empty,
            LocalizedList(item, "tags"),
            Localized(item, "defaultInitPrompt"),
            members);
    }

    private static string BuildResponsibility(string profession, bool isLead)
    {
        if (isLead) return "统筹目标拆解、专家协作、阶段衔接与最终汇总。";
        return string.IsNullOrWhiteSpace(profession)
            ? "完成所属专家角色的专业分析与交付。"
            : $"以{profession}身份完成专业分析、风险识别与可执行建议。";
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
                var value = item.TryGetProperty("zh", out var zh) && zh.ValueKind == JsonValueKind.String ? zh.GetString() : null;
                value ??= item.TryGetProperty("en", out var en) && en.ValueKind == JsonValueKind.String ? en.GetString() : null;
                if (!string.IsNullOrWhiteSpace(value)) result.Add(value);
            }
        }
        return result;
    }
}
