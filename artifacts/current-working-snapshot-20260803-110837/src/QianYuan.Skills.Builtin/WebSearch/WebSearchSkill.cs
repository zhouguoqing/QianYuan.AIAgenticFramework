using System.Text.Json;
using System.Text.Json.Nodes;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;

namespace QianYuan.Skills.Builtin.WebSearch;

/// <summary>
/// Skill exposing two tools to the agent:
///   - web_search(query, topK?) - returns titles + snippets + urls
///   - web_fetch(url) - returns the readable text of a single page
/// </summary>
public sealed class WebSearchSkill : ISkill
{
    private readonly IWebSearchProvider _provider;

    public WebSearchSkill(IWebSearchProvider provider) => _provider = provider;

    public string Id => "qianyuan.websearch";
    public string Name => "Web Search";
    public string Description => "Search the public web and fetch the readable text of any URL.";
    public IReadOnlyList<string> Tags => new[] { "web", "search", "internet", "browse", "news", "research" };
    public string? SystemPromptFragment =>
        "When the user asks about current events, prices, public data, or anything that may have changed recently, " +
        "prefer calling web_search first, then web_fetch on the most promising URL.";

    private static readonly ToolDefinition[] _tools =
    [
        new ToolDefinition
        {
            Name = "web_search",
            Description = "Search the public web. Returns a list of {title, url, snippet}.",
            JsonSchema = """{"type":"object","properties":{"query":{"type":"string","description":"Search query."},"topK":{"type":"integer","description":"How many results (default 5).","default":5}},"required":["query"]}""",
        },
        new ToolDefinition
        {
            Name = "web_fetch",
            Description = "Download a URL and return its readable text (scripts/nav stripped).",
            JsonSchema = """{"type":"object","properties":{"url":{"type":"string","description":"Absolute URL to fetch."}},"required":["url"]}""",
        }
    ];

    public ValueTask<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct = default)
        => ValueTask.FromResult<IReadOnlyList<ToolDefinition>>(_tools);

    public async ValueTask<SkillInvocationResult> InvokeAsync(string toolName, string argumentsJson, SkillInvocationContext context, CancellationToken ct = default)
    {
        var args = JsonNode.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson) ?? new JsonObject();

        switch (toolName)
        {
            case "web_search":
            {
                var q = args["query"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(q)) return SkillInvocationResult.Error("'query' is required");
                var topK = args["topK"]?.GetValue<int>() ?? 5;
                var results = await _provider.SearchAsync(q, topK, ct).ConfigureAwait(false);
                var json = JsonSerializer.Serialize(new
                {
                    provider = _provider.ProviderId,
                    results = results.Select(r => new { r.Title, r.Url, r.Snippet })
                });
                var summary = string.Join("\n", results.Select((r, i) => $"{i + 1}. {r.Title} - {r.Url}"));
                return SkillInvocationResult.Ok(json, summary);
            }
            case "web_fetch":
            {
                var url = args["url"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(url)) return SkillInvocationResult.Error("'url' is required");
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    return SkillInvocationResult.Error($"invalid URL: '{url}' must be an absolute http(s) URL");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(25));
                try
                {
                    var text = await _provider.FetchReadableAsync(url, cts.Token).ConfigureAwait(false);
                    return SkillInvocationResult.Ok(JsonSerializer.Serialize(new { url, text }), $"fetched {url} ({text.Length} chars)");
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return SkillInvocationResult.Error($"web_fetch timed out after 25s for {uri.Host}");
                }
                catch (HttpRequestException ex)
                {
                    return SkillInvocationResult.Error($"web_fetch failed for {uri.Host}: {Flatten(ex)}");
                }
                catch (Exception ex)
                {
                    return SkillInvocationResult.Error($"web_fetch failed for {uri.Host}: {ex.GetType().Name}: {ex.Message}");
                }
            }
            default:
                return SkillInvocationResult.Error($"unknown tool '{toolName}'");
        }
    }

    private static string Flatten(Exception ex)
    {
        var msgs = new List<string>();
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
            msgs.Add(cur.Message);
        return string.Join(" -> ", msgs);
    }
}
