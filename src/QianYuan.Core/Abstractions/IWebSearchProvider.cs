namespace QianYuan.Core.Abstractions;

/// <summary>
/// Abstraction over a web-search backend (Bing, Brave, Tavily, SerpAPI, ...).
/// Surfaced as a tool through the WebSearch skill.
/// </summary>
public interface IWebSearchProvider
{
    string ProviderId { get; }

    Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int topK = 5, CancellationToken ct = default);

    /// <summary>Fetch the readable text of a page (used as a follow-up to a search hit).</summary>
    Task<string> FetchReadableAsync(string url, CancellationToken ct = default);
}

public sealed record WebSearchResult(string Title, string Url, string Snippet, string? Source = null);
