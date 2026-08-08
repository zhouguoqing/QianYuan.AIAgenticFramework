using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Memory;
using QianYuan.Core.Models;

namespace QianYuan.Skills.Builtin.Memory;

public sealed class ConversationMemorySkill : ISkill
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public string Id => "qianyuan.memory";
    public string Name => "QIANYUAN Memory and Conversation Search";
    public string Description => "Read and write local QIANYUAN memory, and search previous conversations for relevant context.";
    public IReadOnlyList<string> Tags => ["memory", "conversation", "search", "history", "context", "qianyuan"];
    public string? SystemPromptFragment =>
        "你可以使用 conversation_search 检索历史会话；使用 memory_read 读取本地项目/用户记忆；当用户明确给出长期偏好、项目约定或可复用事实时，使用 memory_write 写入本地记忆。";

    private static readonly ToolDefinition[] Tools =
    [
        new()
        {
            Name = "conversation_search",
            Description = "Search persisted QIANYUAN conversation history by keyword and optional date range.",
            JsonSchema = """
            {"type":"object","properties":{"query":{"type":"string","description":"Keyword or phrase to search for."},"start_date":{"type":"string","description":"Optional inclusive start date, yyyy-MM-dd."},"end_date":{"type":"string","description":"Optional inclusive end date, yyyy-MM-dd."},"limit":{"type":"integer","minimum":1,"maximum":20,"default":5}},"required":["query"]}
            """
        },
        new()
        {
            Name = "memory_read",
            Description = "Read local QIANYUAN memory. scope can be workspace, user, or daily.",
            JsonSchema = """
            {"type":"object","properties":{"scope":{"type":"string","enum":["workspace","user","daily","all"],"default":"all"}}}
            """
        },
        new()
        {
            Name = "memory_write",
            Description = "Append a concise durable note to local QIANYUAN memory. Use only for stable preferences, project rules, reusable facts, or completed-work notes.",
            JsonSchema = """
            {"type":"object","properties":{"scope":{"type":"string","enum":["workspace","user","daily"],"default":"workspace"},"content":{"type":"string","description":"Concise memory text to append."}},"required":["content"]}
            """
        }
    ];

    public ValueTask<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct = default) =>
        ValueTask.FromResult<IReadOnlyList<ToolDefinition>>(Tools);

    public async ValueTask<SkillInvocationResult> InvokeAsync(string toolName, string argumentsJson, SkillInvocationContext context, CancellationToken ct = default)
    {
        return toolName switch
        {
            "conversation_search" => await SearchConversationsAsync(argumentsJson, context, ct).ConfigureAwait(false),
            "memory_read" => await ReadMemoryAsync(argumentsJson, context, ct).ConfigureAwait(false),
            "memory_write" => await WriteMemoryAsync(argumentsJson, context, ct).ConfigureAwait(false),
            _ => SkillInvocationResult.Error($"Unknown memory tool: {toolName}"),
        };
    }

    private static async ValueTask<SkillInvocationResult> SearchConversationsAsync(string argumentsJson, SkillInvocationContext context, CancellationToken ct)
    {
        var args = ParseArgs(argumentsJson);
        var query = GetString(args, "query")?.Trim();
        if (string.IsNullOrWhiteSpace(query)) return SkillInvocationResult.Error("query is required.");

        var limit = Math.Clamp(GetInt(args, "limit") ?? 5, 1, 20);
        var start = ParseDate(GetString(args, "start_date"));
        var end = ParseDate(GetString(args, "end_date"))?.AddDays(1).AddTicks(-1);
        var ownerId = context.Metadata is not null && context.Metadata.TryGetValue("ownerId", out var owner) ? owner : null;
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        using var scope = context.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var summaries = await sessions.ListAsync(ownerId, 200, ct).ConfigureAwait(false);
        var matches = new List<object>();

        foreach (var summary in summaries)
        {
            if (start is not null && summary.UpdatedAt.UtcDateTime < start.Value) continue;
            if (end is not null && summary.UpdatedAt.UtcDateTime > end.Value) continue;
            var state = await sessions.GetAsync(summary.SessionId, ct).ConfigureAwait(false);
            if (state is null) continue;

            var snippets = new List<object>();
            foreach (var message in state.Messages)
            {
                var text = MessageText(message);
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (!Matches(text, terms)) continue;
                snippets.Add(new
                {
                    role = message.Role.ToString(),
                    text = Snippet(text, 500),
                });
                if (snippets.Count >= 3) break;
            }

            if (snippets.Count == 0 && !Matches(summary.Title ?? string.Empty, terms)) continue;
            matches.Add(new
            {
                sessionId = summary.SessionId,
                title = summary.Title,
                agentId = summary.AgentId,
                updatedAt = summary.UpdatedAt,
                messageCount = summary.MessageCount,
                snippets,
            });
            if (matches.Count >= limit) break;
        }

        var json = JsonSerializer.Serialize(new { query, count = matches.Count, matches }, JsonOptions);
        return SkillInvocationResult.Ok(json, matches.Count == 0 ? "未找到相关历史会话。" : $"找到 {matches.Count} 条相关历史会话。 ");
    }

    private static async ValueTask<SkillInvocationResult> ReadMemoryAsync(string argumentsJson, SkillInvocationContext context, CancellationToken ct)
    {
        var args = ParseArgs(argumentsJson);
        var scope = (GetString(args, "scope") ?? "all").Trim().ToLowerInvariant();
        var memory = context.Services.GetRequiredService<IMemoryService>();
        var snapshot = await memory.ReadAsync(BuildContext(context), ct).ConfigureAwait(false);

        object body = scope switch
        {
            "user" => new { scope, content = snapshot.UserMemory, path = snapshot.UserMemoryPath },
            "workspace" => new { scope, content = snapshot.WorkspaceMemory, path = snapshot.WorkspaceMemoryPath },
            "daily" => new { scope, content = snapshot.TodayLog, path = snapshot.DailyLogPath },
            _ => new { scope = "all", user = snapshot.UserMemory, workspace = snapshot.WorkspaceMemory, daily = snapshot.TodayLog },
        };
        return SkillInvocationResult.Ok(JsonSerializer.Serialize(body, JsonOptions), "已读取本地记忆。 ");
    }

    private static async ValueTask<SkillInvocationResult> WriteMemoryAsync(string argumentsJson, SkillInvocationContext context, CancellationToken ct)
    {
        var args = ParseArgs(argumentsJson);
        var content = GetString(args, "content")?.Trim();
        if (string.IsNullOrWhiteSpace(content)) return SkillInvocationResult.Error("content is required.");

        var scope = GetString(args, "scope") ?? "workspace";
        var memory = context.Services.GetRequiredService<IMemoryService>();
        await memory.WriteMemoryAsync(BuildContext(context), scope, content, ct).ConfigureAwait(false);
        return SkillInvocationResult.Ok(JsonSerializer.Serialize(new { ok = true, scope }, JsonOptions), $"已写入 {scope} 记忆。 ");
    }

    private static MemoryContext BuildContext(SkillInvocationContext context)
    {
        string? workspacePath = null;
        string? workspaceLabel = null;
        string? ownerId = null;
        context.Metadata?.TryGetValue("workspacePath", out workspacePath);
        context.Metadata?.TryGetValue("workspaceLabel", out workspaceLabel);
        context.Metadata?.TryGetValue("ownerId", out ownerId);
        return new MemoryContext(workspacePath, workspaceLabel, ownerId, context.SessionId);
    }

    private static Dictionary<string, JsonElement> ParseArgs(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return [];
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? GetString(IReadOnlyDictionary<string, JsonElement> args, string name) =>
        args.TryGetValue(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : null;

    private static int? GetInt(IReadOnlyDictionary<string, JsonElement> args, string name)
    {
        if (!args.TryGetValue(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    }

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, out var parsed) ? DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc) : null;

    private static bool Matches(string text, string[] terms) =>
        terms.Length == 0 || terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string MessageText(ChatMessage message)
    {
        var builder = new StringBuilder();
        foreach (var part in message.Parts)
        {
            if (!string.IsNullOrWhiteSpace(part.Text)) builder.Append(part.Text).Append(' ');
            else if (!string.IsNullOrWhiteSpace(part.JsonPayload)) builder.Append(part.JsonPayload).Append(' ');
        }
        return builder.ToString().Trim();
    }

    private static string Snippet(string value, int max) => value.Length <= max ? value : value[..max] + "...";
}