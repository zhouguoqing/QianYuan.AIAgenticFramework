using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using QianYuan.Core.Abstractions;

namespace QianYuan.Skills.Builtin.WebSearch;

/// <summary>
/// Reads an HTTP response body as text, tolerating exotic charsets like GBK / GB2312 / Big5.
/// Falls back to UTF-8 when the declared charset is missing or rejected by the runtime.
/// </summary>
internal static class HttpBodyReader
{
    private static readonly object _gate = new();
    private static bool _registered;

    public static async Task<string> ReadStringAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        EnsureCodePagesRegistered();
        try
        {
            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Bad / unsupported charset in Content-Type header — recover from raw bytes.
        }

        var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var charset = resp.Content.Headers.ContentType?.CharSet?.Trim('"', '\'', ' ');
        if (!string.IsNullOrEmpty(charset))
        {
            try { return Encoding.GetEncoding(charset).GetString(bytes); }
            catch { /* fall through */ }
        }
        return Encoding.UTF8.GetString(bytes);
    }

    private static void EnsureCodePagesRegistered()
    {
        if (_registered) return;
        lock (_gate)
        {
            if (_registered) return;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _registered = true;
        }
    }
}

/// <summary>
/// Free, key-less DuckDuckGo search backend that scrapes the HTML endpoint
/// (https://html.duckduckgo.com/html/). Good as a zero-config default.
/// Throughput is rate-limited by DDG; for heavy use prefer Tavily/Bing/Brave.
/// </summary>
public sealed class DuckDuckGoSearchProvider : IWebSearchProvider
{
    private readonly HttpClient _http;
    public string ProviderId => "duckduckgo";

    private const string Endpoint = "https://html.duckduckgo.com/html/";
    private const string DefaultUserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 13_0) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/126.0 Safari/537.36";

    public DuckDuckGoSearchProvider(HttpClient http)
    {
        _http = http;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);
        if (!_http.DefaultRequestHeaders.Accept.Any())
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
    }

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int topK = 5, CancellationToken ct = default)
    {
        // POST is more reliable than GET against the HTML endpoint.
        using var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("q", query),
            new KeyValuePair<string, string>("kl", "wt-wt"),
        });
        using var resp = await _http.PostAsync(Endpoint, body, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var html = await HttpBodyReader.ReadStringAsync(resp, ct).ConfigureAwait(false);

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html);
        var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'result') and not(contains(@class,'result--ad'))]");
        var list = new List<WebSearchResult>();
        if (nodes is null) return list;

        foreach (var n in nodes)
        {
            if (list.Count >= topK) break;
            var anchor = n.SelectSingleNode(".//a[contains(@class,'result__a')]");
            if (anchor is null) continue;
            var title = HtmlAgilityPack.HtmlEntity.DeEntitize(anchor.InnerText ?? "").Trim();
            var rawHref = anchor.GetAttributeValue("href", "");
            var url = UnwrapDdgRedirect(rawHref);
            if (string.IsNullOrWhiteSpace(url)) continue;

            var snippetNode = n.SelectSingleNode(".//*[contains(@class,'result__snippet')]");
            var snippet = HtmlAgilityPack.HtmlEntity.DeEntitize(snippetNode?.InnerText ?? "").Trim();

            list.Add(new WebSearchResult(title, url, snippet, "duckduckgo"));
        }
        return list;
    }

    public async Task<string> FetchReadableAsync(string url, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var html = await HttpBodyReader.ReadStringAsync(resp, ct).ConfigureAwait(false);
        return ReadabilityHelper.ToReadableText(html);
    }

    /// <summary>
    /// DDG wraps every outbound link as //duckduckgo.com/l/?uddg=ENCODED&amp;rut=... .
    /// Decode the uddg param to recover the real destination.
    /// </summary>
    private static string UnwrapDdgRedirect(string href)
    {
        if (string.IsNullOrEmpty(href)) return "";
        // Normalize scheme-relative form.
        var full = href.StartsWith("//", StringComparison.Ordinal) ? "https:" + href : href;
        if (!Uri.TryCreate(full, UriKind.Absolute, out var uri)) return href;
        if (uri.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/l/", StringComparison.OrdinalIgnoreCase))
        {
            var qs = HttpUtility.ParseQueryString(uri.Query);
            var target = qs["uddg"];
            if (!string.IsNullOrEmpty(target)) return target;
        }
        return full;
    }
}

/// <summary>Tavily search backend (https://tavily.com).</summary>
public sealed class TavilySearchProvider : IWebSearchProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    public string ProviderId => "tavily";

    public TavilySearchProvider(HttpClient http, string apiKey)
    {
        _http = http; _apiKey = apiKey;
    }

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int topK = 5, CancellationToken ct = default)
    {
        var body = new { api_key = _apiKey, query, max_results = topK, search_depth = "basic" };
        using var resp = await _http.PostAsJsonAsync("https://api.tavily.com/search", body, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct).ConfigureAwait(false)!;
        var arr = json["results"]?.AsArray();
        var list = new List<WebSearchResult>();
        if (arr is not null)
            foreach (var r in arr)
                list.Add(new WebSearchResult(
                    r?["title"]?.GetValue<string>() ?? "",
                    r?["url"]?.GetValue<string>() ?? "",
                    r?["content"]?.GetValue<string>() ?? "",
                    "tavily"));
        return list;
    }

    public async Task<string> FetchReadableAsync(string url, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var html = await HttpBodyReader.ReadStringAsync(resp, ct).ConfigureAwait(false);
        return ReadabilityHelper.ToReadableText(html);
    }
}

/// <summary>Bing Web Search v7 backend.</summary>
public sealed class BingSearchProvider : IWebSearchProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    public string ProviderId => "bing";

    public BingSearchProvider(HttpClient http, string apiKey)
    {
        _http = http; _apiKey = apiKey;
        if (!_http.DefaultRequestHeaders.Contains("Ocp-Apim-Subscription-Key"))
            _http.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _apiKey);
    }

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int topK = 5, CancellationToken ct = default)
    {
        var url = $"https://api.bing.microsoft.com/v7.0/search?q={Uri.EscapeDataString(query)}&count={topK}";
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct).ConfigureAwait(false)!;
        var arr = json["webPages"]?["value"]?.AsArray();
        var list = new List<WebSearchResult>();
        if (arr is not null)
            foreach (var r in arr)
                list.Add(new WebSearchResult(
                    r?["name"]?.GetValue<string>() ?? "",
                    r?["url"]?.GetValue<string>() ?? "",
                    r?["snippet"]?.GetValue<string>() ?? "",
                    "bing"));
        return list;
    }

    public async Task<string> FetchReadableAsync(string url, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var html = await HttpBodyReader.ReadStringAsync(resp, ct).ConfigureAwait(false);
        return ReadabilityHelper.ToReadableText(html);
    }
}

/// <summary>Brave Search backend (https://search.brave.com/api).</summary>
public sealed class BraveSearchProvider : IWebSearchProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    public string ProviderId => "brave";

    public BraveSearchProvider(HttpClient http, string apiKey)
    {
        _http = http; _apiKey = apiKey;
        if (!_http.DefaultRequestHeaders.Contains("X-Subscription-Token"))
            _http.DefaultRequestHeaders.Add("X-Subscription-Token", _apiKey);
        if (!_http.DefaultRequestHeaders.Contains("Accept"))
            _http.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int topK = 5, CancellationToken ct = default)
    {
        var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={topK}";
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: ct).ConfigureAwait(false)!;
        var arr = json["web"]?["results"]?.AsArray();
        var list = new List<WebSearchResult>();
        if (arr is not null)
            foreach (var r in arr)
                list.Add(new WebSearchResult(
                    r?["title"]?.GetValue<string>() ?? "",
                    r?["url"]?.GetValue<string>() ?? "",
                    r?["description"]?.GetValue<string>() ?? "",
                    "brave"));
        return list;
    }

    public async Task<string> FetchReadableAsync(string url, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var html = await HttpBodyReader.ReadStringAsync(resp, ct).ConfigureAwait(false);
        return ReadabilityHelper.ToReadableText(html);
    }
}

internal static class ReadabilityHelper
{
    public static string ToReadableText(string html)
    {
        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html);
        // Strip script/style/nav/footer/aside before extracting text.
        foreach (var xpath in new[] { "//script", "//style", "//nav", "//footer", "//aside", "//noscript" })
        {
            var nodes = doc.DocumentNode.SelectNodes(xpath);
            if (nodes is null) continue;
            foreach (var n in nodes.ToList()) n.Remove();
        }
        var body = doc.DocumentNode.SelectSingleNode("//main") ?? doc.DocumentNode.SelectSingleNode("//article") ?? doc.DocumentNode;
        var text = HtmlAgilityPack.HtmlEntity.DeEntitize(body.InnerText);
        // Collapse whitespace runs.
        var collapsed = System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
        return collapsed.Length > 8000 ? collapsed[..8000] + "..." : collapsed;
    }
}
