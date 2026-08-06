using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using QianYuan.Api.Models;
using QianYuan.Core.Abstractions;
using QianYuan.Data;
using QianYuan.Data.Entities;
using QianYuan.Kernel.Skills;

namespace QianYuan.Api.Services;

public interface ISkillMarketplaceService
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SkillPackageDto>> ListMarketAsync(string? category, string? q, CancellationToken ct = default);
    Task<IReadOnlyList<SkillCategoryDto>> ListCategoriesAsync(CancellationToken ct = default);
    Task<InstalledSkillDto> InstallAsync(InstallSkillRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<InstalledSkillDto>> ListInstalledAsync(CancellationToken ct = default);
    Task<InstalledSkillDto> CreateAsync(CreateSkillRequest request, CancellationToken ct = default);
    Task<bool> SetEnabledAsync(string skillId, bool enabled, CancellationToken ct = default);
    Task<bool> UninstallAsync(string skillId, CancellationToken ct = default);
    Task<IReadOnlyList<SkillMarketEntryDto>> SearchAsync(string? q, string? category, CancellationToken ct = default);
}

public sealed class SkillMarketplaceService : ISkillMarketplaceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly QianYuanDbContext _db;
    private readonly ISkillManager _skills;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SkillMarketplaceService> _logger;

    public SkillMarketplaceService(QianYuanDbContext db, ISkillManager skills, IWebHostEnvironment env, ILogger<SkillMarketplaceService> logger)
    {
        _db = db;
        _skills = skills;
        _env = env;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await SeedMarketAsync(ct);
        await RegisterInstalledSkillsAsync(ct);
        await ApplyPersistedEnabledStatesAsync(ct);
    }

    public async Task<IReadOnlyList<SkillPackageDto>> ListMarketAsync(string? category, string? q, CancellationToken ct = default)
    {
        var entries = await QueryMarket(category, q).ToListAsync(ct);
        var installed = await _db.InstalledSkills.AsNoTracking().Where(s => s.MarketEntryId != null).ToDictionaryAsync(s => s.MarketEntryId!, s => s, StringComparer.OrdinalIgnoreCase, ct);
        var packages = entries
            .GroupBy(e => e.Package)
            .OrderBy(g => g.Key.SortOrder)
            .ThenBy(g => g.Key.Name)
            .Select(g => new SkillPackageDto(
                g.Key.Id,
                g.Key.Name,
                g.Key.Description,
                g.Key.Category,
                g.Key.SortOrder,
                g.OrderBy(e => e.SortOrder).ThenBy(e => e.Name).Select(e => ToMarketDto(e, installed)).ToList()))
            .ToList();
        return packages;
    }

    public async Task<IReadOnlyList<SkillMarketEntryDto>> SearchAsync(string? q, string? category, CancellationToken ct = default)
    {
        var entries = await QueryMarket(category, q).OrderBy(e => e.SortOrder).ThenBy(e => e.Name).ToListAsync(ct);
        var installed = await _db.InstalledSkills.AsNoTracking().Where(s => s.MarketEntryId != null).ToDictionaryAsync(s => s.MarketEntryId!, s => s, StringComparer.OrdinalIgnoreCase, ct);
        return entries.Select(e => ToMarketDto(e, installed)).ToList();
    }

    public async Task<IReadOnlyList<SkillCategoryDto>> ListCategoriesAsync(CancellationToken ct = default)
    {
        var market = await _db.SkillMarketEntries.AsNoTracking()
            .Where(e => e.Enabled)
            .GroupBy(e => e.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var installed = await _db.InstalledSkills.AsNoTracking()
            .GroupBy(e => e.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var keys = market.Select(x => x.Category).Concat(installed.Select(x => x.Category)).Concat(_skills.ListManifests().Select(m => m.Category)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        return keys.Select(key => new SkillCategoryDto(key, ToDisplayName(key), market.FirstOrDefault(x => Same(x.Category, key))?.Count ?? 0, installed.FirstOrDefault(x => Same(x.Category, key))?.Count ?? 0)).ToList();
    }

    public async Task<IReadOnlyList<InstalledSkillDto>> ListInstalledAsync(CancellationToken ct = default)
    {
        var rows = await _db.InstalledSkills.AsNoTracking().OrderBy(s => s.Category).ThenBy(s => s.Name).ToListAsync(ct);
        return rows.Select(ToInstalledDto).ToList();
    }

    public async Task<InstalledSkillDto> InstallAsync(InstallSkillRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.MarketEntryId)) throw new ArgumentException("MarketEntryId is required.");
        var entry = await _db.SkillMarketEntries.Include(e => e.Package).FirstOrDefaultAsync(e => e.Id == request.MarketEntryId && e.Enabled, ct)
            ?? throw new KeyNotFoundException("Market skill not found.");
        var skillId = $"market.{Slug(entry.Id)}";
        var installDir = Path.Combine(GetSkillsRoot(), "market", Slug(entry.Id));
        Directory.CreateDirectory(installDir);
        var skillFile = Path.Combine(installDir, "SKILL.md");
        var markdown = string.IsNullOrWhiteSpace(entry.SkillMarkdown) ? BuildSkillMarkdown(skillId, entry.Name, entry.Description, entry.Category, ReadJsonList(entry.TagsJson), ReadJsonList(entry.TriggerPhrasesJson), entry.Description) : entry.SkillMarkdown;
        await File.WriteAllTextAsync(skillFile, markdown, Encoding.UTF8, ct);
        RegisterMarkdownSkill(skillFile, Path.Combine(GetSkillsRoot(), "market"), request.Enabled);

        var row = await _db.InstalledSkills.FirstOrDefaultAsync(s => s.SkillId == skillId, ct);
        var now = DateTime.UtcNow;
        if (row is null)
        {
            row = new InstalledSkill { SkillId = skillId, InstalledAt = now };
            _db.InstalledSkills.Add(row);
        }
        row.MarketEntryId = entry.Id;
        row.Name = entry.Name;
        row.Description = entry.Description;
        row.Category = entry.Category;
        row.TagsJson = entry.TagsJson;
        row.TriggerPhrasesJson = entry.TriggerPhrasesJson;
        row.Scope = "market";
        row.InstallPath = MakeRelativePath(skillFile);
        row.Enabled = request.Enabled;
        row.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        await WriteSkillsLockAsync(ct);
        return ToInstalledDto(row);
    }

    public async Task<InstalledSkillDto> CreateAsync(CreateSkillRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Name is required.");
        if (string.IsNullOrWhiteSpace(request.Body)) throw new ArgumentException("Body is required.");
        var slug = Slug(string.IsNullOrWhiteSpace(request.Id) ? request.Name : request.Id);
        var skillId = slug.StartsWith("custom.", StringComparison.OrdinalIgnoreCase) ? slug : $"custom.{slug}";
        var category = NormalizeCategory(request.Category);
        var tags = CleanList(request.Tags);
        var triggers = CleanList(request.TriggerPhrases);
        var installDir = Path.Combine(GetSkillsRoot(), "custom", Slug(skillId));
        Directory.CreateDirectory(installDir);
        var skillFile = Path.Combine(installDir, "SKILL.md");
        var markdown = BuildSkillMarkdown(skillId, request.Name.Trim(), request.Description?.Trim() ?? request.Name.Trim(), category, tags, triggers, request.Body.Trim());
        await File.WriteAllTextAsync(skillFile, markdown, Encoding.UTF8, ct);
        RegisterMarkdownSkill(skillFile, Path.Combine(GetSkillsRoot(), "custom"), true);

        var now = DateTime.UtcNow;
        var row = await _db.InstalledSkills.FirstOrDefaultAsync(s => s.SkillId == skillId, ct);
        if (row is null)
        {
            row = new InstalledSkill { SkillId = skillId, InstalledAt = now };
            _db.InstalledSkills.Add(row);
        }
        row.MarketEntryId = null;
        row.Name = request.Name.Trim();
        row.Description = request.Description?.Trim() ?? request.Name.Trim();
        row.Category = category;
        row.TagsJson = JsonSerializer.Serialize(tags, JsonOptions);
        row.TriggerPhrasesJson = JsonSerializer.Serialize(triggers, JsonOptions);
        row.Scope = string.IsNullOrWhiteSpace(request.Scope) ? "user" : request.Scope.Trim();
        row.InstallPath = MakeRelativePath(skillFile);
        row.Enabled = true;
        row.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        await WriteSkillsLockAsync(ct);
        return ToInstalledDto(row);
    }

    public async Task<bool> SetEnabledAsync(string skillId, bool enabled, CancellationToken ct = default)
    {
        var manifest = _skills.ListManifests().FirstOrDefault(m => Same(m.Id, skillId));
        if (manifest is null) return false;
        _skills.SetEnabled(skillId, enabled);
        var row = await _db.InstalledSkills.FirstOrDefaultAsync(s => s.SkillId == skillId, ct);
        var now = DateTime.UtcNow;
        if (row is null)
        {
            row = new InstalledSkill
            {
                SkillId = skillId,
                Name = manifest.Name,
                Description = manifest.Description,
                Category = manifest.Category,
                TagsJson = JsonSerializer.Serialize(manifest.Tags, JsonOptions),
                TriggerPhrasesJson = JsonSerializer.Serialize(manifest.TriggerPhrases ?? Array.Empty<string>(), JsonOptions),
                Scope = "registry",
                InstallPath = string.Empty,
                InstalledAt = now,
            };
            _db.InstalledSkills.Add(row);
        }
        row.Enabled = enabled;
        row.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        await WriteSkillsLockAsync(ct);
        return true;
    }

    public async Task<bool> UninstallAsync(string skillId, CancellationToken ct = default)
    {
        var row = await _db.InstalledSkills.FirstOrDefaultAsync(s => s.SkillId == skillId, ct);
        if (row is null) return false;
        _skills.Unregister(skillId);
        TryDeleteInstalledFile(row.InstallPath);
        _db.InstalledSkills.Remove(row);
        await _db.SaveChangesAsync(ct);
        await WriteSkillsLockAsync(ct);
        return true;
    }


    private IQueryable<SkillMarketEntry> QueryMarket(string? category, string? q)
    {
        var query = _db.SkillMarketEntries.AsNoTracking().Include(e => e.Package).Where(e => e.Enabled);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(e => e.Category == category);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(e => e.Name.ToLower().Contains(term) || e.Description.ToLower().Contains(term) || e.TagsJson.ToLower().Contains(term) || e.TriggerPhrasesJson.ToLower().Contains(term));
        }
        return query;
    }

    private async Task SeedMarketAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        foreach (var package in SeedPackages())
        {
            var existing = await _db.SkillPackages.FirstOrDefaultAsync(p => p.Id == package.Id, ct);
            if (existing is null)
            {
                _db.SkillPackages.Add(new SkillPackage { Id = package.Id, Name = package.Name, Description = package.Description, Category = package.Category, SortOrder = package.SortOrder, CreatedAt = now, UpdatedAt = now });
            }
        }
        foreach (var entry in SeedEntries())
        {
            var existing = await _db.SkillMarketEntries.FirstOrDefaultAsync(e => e.Id == entry.Id, ct);
            if (existing is null)
            {
                _db.SkillMarketEntries.Add(new SkillMarketEntry
                {
                    Id = entry.Id,
                    PackageId = entry.PackageId,
                    Name = entry.Name,
                    Description = entry.Description,
                    Category = entry.Category,
                    TagsJson = JsonSerializer.Serialize(entry.Tags, JsonOptions),
                    TriggerPhrasesJson = JsonSerializer.Serialize(entry.Triggers, JsonOptions),
                    SkillMarkdown = BuildSkillMarkdown($"market.{Slug(entry.Id)}", entry.Name, entry.Description, entry.Category, entry.Tags, entry.Triggers, entry.Body),
                    Source = "qianyuan-market-local",
                    SourceUrl = $"qianyuan://skills/{entry.PackageId}/{entry.Id}",
                    Enabled = true,
                    SortOrder = entry.SortOrder,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task RegisterInstalledSkillsAsync(CancellationToken ct)
    {
        var rows = await _db.InstalledSkills.AsNoTracking().Where(s => !string.IsNullOrWhiteSpace(s.InstallPath)).ToListAsync(ct);
        foreach (var row in rows)
        {
            var fullPath = ResolveRepoPath(row.InstallPath);
            if (!File.Exists(fullPath)) continue;
            try
            {
                RegisterMarkdownSkill(fullPath, Directory.GetParent(Path.GetDirectoryName(fullPath)!)?.FullName, row.Enabled);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to register installed skill {SkillId} from {Path}.", row.SkillId, fullPath);
            }
        }
    }

    private async Task ApplyPersistedEnabledStatesAsync(CancellationToken ct)
    {
        var rows = await _db.InstalledSkills.AsNoTracking().ToListAsync(ct);
        foreach (var row in rows)
        {
            if (_skills.ListManifests().Any(m => Same(m.Id, row.SkillId))) _skills.SetEnabled(row.SkillId, row.Enabled);
        }
    }

    private MarkdownSkill RegisterMarkdownSkill(string skillFile, string? rootDirectory, bool enabled)
    {
        var skill = MarkdownSkillLoader.LoadFromFile(skillFile, rootDirectory, "market");
        _skills.Register(skill);
        _skills.SetEnabled(skill.Id, enabled);
        return skill;
    }

    private async Task WriteSkillsLockAsync(CancellationToken ct)
    {
        var lockPath = Path.Combine(GetRepoRoot(), "skills-lock.json");
        Dictionary<string, object?> root;
        if (File.Exists(lockPath))
        {
            try
            {
                root = JsonSerializer.Deserialize<Dictionary<string, object?>>(await File.ReadAllTextAsync(lockPath, ct), JsonOptions) ?? new();
            }
            catch
            {
                root = new();
            }
        }
        else
        {
            root = new();
        }
        root["version"] = 1;
        var skills = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetValue("skills", out var existing) && existing is JsonElement el && el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject()) skills[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText(), JsonOptions);
        }
        var rows = await _db.InstalledSkills.AsNoTracking().ToListAsync(ct);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.InstallPath)) continue;
            var full = ResolveRepoPath(row.InstallPath);
            skills[row.SkillId] = new
            {
                source = row.MarketEntryId is null ? "qianyuan-custom" : "qianyuan-market-local",
                sourceType = row.MarketEntryId is null ? "custom" : "market",
                skillPath = row.InstallPath.Replace('\\', '/'),
                enabled = row.Enabled,
                computedHash = File.Exists(full) ? ComputeSha256(full) : string.Empty,
            };
        }
        root["skills"] = skills;
        await File.WriteAllTextAsync(lockPath, JsonSerializer.Serialize(root, JsonOptions), Encoding.UTF8, ct);
    }

    private SkillMarketEntryDto ToMarketDto(SkillMarketEntry entry, IReadOnlyDictionary<string, InstalledSkill> installed)
    {
        installed.TryGetValue(entry.Id, out var row);
        return new SkillMarketEntryDto(entry.Id, entry.PackageId, entry.Package.Name, entry.Name, entry.Description, entry.Category, ReadJsonList(entry.TagsJson), ReadJsonList(entry.TriggerPhrasesJson), entry.Source, entry.SourceUrl, row is not null, row?.SkillId, row?.Enabled ?? false);
    }

    private static InstalledSkillDto ToInstalledDto(InstalledSkill row) =>
        new(row.SkillId, row.MarketEntryId, row.Name, row.Description, row.Category, ReadJsonList(row.TagsJson), ReadJsonList(row.TriggerPhrasesJson), row.Scope, row.InstallPath, row.Enabled, row.InstalledAt, row.UpdatedAt);

    private static string BuildSkillMarkdown(string id, string name, string description, string category, IReadOnlyList<string> tags, IReadOnlyList<string> triggers, string body)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"id: {id}");
        sb.AppendLine($"name: {EscapeYaml(name)}");
        sb.AppendLine($"description: {EscapeYaml(description)}");
        sb.AppendLine($"category: {EscapeYaml(category)}");
        if (tags.Count > 0) sb.AppendLine($"tags: {string.Join(", ", tags.Select(EscapeYaml))}");
        if (triggers.Count > 0) sb.AppendLine($"triggers: {string.Join(", ", triggers.Select(EscapeYaml))}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(body.Trim());
        sb.AppendLine();
        return sb.ToString();
    }

    private static string EscapeYaml(string value) => value.Contains(':') || value.Contains(',') ? JsonSerializer.Serialize(value) : value;

    private string GetSkillsRoot()
    {
        var root = Path.Combine(GetRepoRoot(), "skills");
        Directory.CreateDirectory(root);
        return root;
    }

    private string GetRepoRoot()
    {
        var dir = new DirectoryInfo(_env.ContentRootPath);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "QianYuan.AgenticFramework.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        return Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", ".."));
    }

    private string MakeRelativePath(string fullPath) => Path.GetRelativePath(GetRepoRoot(), fullPath).Replace('\\', '/');
    private string ResolveRepoPath(string path) => Path.IsPathRooted(path) ? path : Path.Combine(GetRepoRoot(), path.Replace('/', Path.DirectorySeparatorChar));

    private void TryDeleteInstalledFile(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath)) return;
        var full = Path.GetFullPath(ResolveRepoPath(installPath));
        var skillsRoot = Path.GetFullPath(GetSkillsRoot());
        if (!full.StartsWith(skillsRoot, StringComparison.OrdinalIgnoreCase)) return;
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) && !Same(Path.GetFullPath(dir), skillsRoot))
        {
            Directory.Delete(dir, recursive: true);
            return;
        }
        if (File.Exists(full)) File.Delete(full);
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static IReadOnlyList<string> ReadJsonList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static IReadOnlyList<string> CleanList(IReadOnlyList<string>? values) =>
        values?.Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();

    private static string NormalizeCategory(string? category) => string.IsNullOrWhiteSpace(category) ? "general" : Slug(category);

    private static string Slug(string value)
    {
        var normalized = Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9._-]+", "-").Trim('-', '.', '_');
        return string.IsNullOrWhiteSpace(normalized) ? "skill" : normalized;
    }

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string ToDisplayName(string category) => string.Join(' ', category.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries).Select(s => char.ToUpperInvariant(s[0]) + s[1..]));

    private static IReadOnlyList<SeedPackage> SeedPackages() =>
    [
        new("qianyuan-productivity", "QIANYUAN Productivity Skills", "Core planning, summary, file search, and scheduling skills.", "productivity", 10),
        new("qianyuan-development", "QIANYUAN Development Skills", "Coding, debugging, review, and repository workflow skills.", "development", 20),
        new("qianyuan-visualization", "QIANYUAN Visualization Skills", "Diagram, report, and data visualization prompt skills.", "visualization", 30),
        new("qianyuan-research", "QIANYUAN Research Skills", "Research, evidence collection, and market analysis skills.", "research", 40),
    ];

    private static IReadOnlyList<SeedEntry> SeedEntries() =>
    [
        new("task-planner", "qianyuan-productivity", "Task Planner", "Break goals into prioritized tasks, milestones, owners, and risks.", "productivity", ["planning", "tasks", "milestones"], ["plan my tasks", "prioritize work", "make a roadmap"], "Use this skill to turn a goal into a concise execution plan. Ask for missing constraints, then output milestones, owners, risks, and next actions.", 10),
        new("meeting-summarizer", "qianyuan-productivity", "Meeting Summarizer", "Summarize meeting notes into decisions, action items, and open questions.", "productivity", ["meeting", "summary", "actions"], ["summarize meeting", "extract action items"], "Use this skill for meeting transcripts or notes. Produce sections: decisions, action items, owners, deadlines, risks, and follow-ups.", 20),
        new("file-search-analyst", "qianyuan-productivity", "File Search Analyst", "Guide local file search, evidence extraction, and citation-friendly synthesis.", "document", ["files", "search", "documents"], ["find files", "search documents", "locate evidence"], "Use this skill when the user asks to find or compare local files. Suggest precise search patterns, inspect only relevant files, and summarize findings with file paths.", 30),
        new("scheduled-workflow", "qianyuan-productivity", "Scheduled Workflow Designer", "Design repeatable scheduled tasks, reminders, and automation checkpoints.", "automation", ["schedule", "automation", "reminder"], ["schedule a task", "recurring workflow", "automation plan"], "Use this skill to design time-based workflows. Output schedule, trigger, inputs, expected output, failure handling, and observability.", 40),
        new("code-implementation-plan", "qianyuan-development", "Code Implementation Plan", "Plan safe code changes with files, tests, rollback, and validation steps.", "development", ["code", "implementation", "plan"], ["implement this feature", "code plan", "change plan"], "Use this skill before coding. Identify files, smallest safe changes, risks, tests, and rollback strategy. Keep scope tight.", 10),
        new("systematic-debugging", "qianyuan-development", "Systematic Debugging", "Diagnose bugs through reproduction, hypothesis, instrumentation, and verification.", "development", ["debug", "bug", "diagnosis"], ["debug this", "fix bug", "investigate error"], "Use this skill for bug reports. Reproduce, isolate, form hypotheses, test them, fix root cause, and verify with targeted tests.", 20),
        new("code-review-checklist", "qianyuan-development", "Code Review Checklist", "Review correctness, security, performance, maintainability, and tests.", "development", ["review", "quality", "security"], ["review code", "check this patch"], "Use this skill for code review. Focus on concrete defects, cite files, rank severity, and avoid style-only noise unless requested.", 30),
        new("git-workflow", "qianyuan-development", "Git Workflow Assistant", "Plan branches, commits, diffs, and safe rollback workflows.", "development", ["git", "branch", "commit"], ["git workflow", "prepare commit", "rollback changes"], "Use this skill for Git operations. Inspect status first, avoid destructive commands, and explain reversible steps.", 40),
        new("diagram-designer", "qianyuan-visualization", "Diagram Designer", "Convert requirements into Mermaid-ready flowcharts, sequence diagrams, and architecture diagrams.", "visualization", ["diagram", "mermaid", "architecture"], ["draw diagram", "make mermaid", "architecture diagram"], "Use this skill to produce clear diagrams. Choose the right diagram type, keep labels concise, and include a short legend.", 10),
        new("data-visualization-planner", "qianyuan-visualization", "Data Visualization Planner", "Choose charts, metrics, and dashboard layouts for analytical reports.", "visualization", ["chart", "dashboard", "metrics"], ["visualize data", "dashboard plan", "chart selection"], "Use this skill for data presentation. Recommend chart types, required fields, caveats, and dashboard layout.", 20),
        new("market-research-brief", "qianyuan-research", "Market Research Brief", "Structure competitor, customer, policy, and trend research into an evidence brief.", "research", ["market", "research", "competitor"], ["market research", "competitor analysis", "industry brief"], "Use this skill for market or competitor research. Separate facts, assumptions, evidence gaps, and recommended next research steps.", 10),
        new("evidence-synthesis", "qianyuan-research", "Evidence Synthesis", "Merge multiple materials into claims, evidence, confidence, and next actions.", "research", ["evidence", "synthesis", "analysis"], ["synthesize evidence", "analyze materials", "confidence scoring"], "Use this skill when many materials must be summarized. Group claims, cite source snippets or file paths, score confidence, and list gaps.", 20),
    ];

    private sealed record SeedPackage(string Id, string Name, string Description, string Category, int SortOrder);
    private sealed record SeedEntry(string Id, string PackageId, string Name, string Description, string Category, IReadOnlyList<string> Tags, IReadOnlyList<string> Triggers, string Body, int SortOrder);
}
