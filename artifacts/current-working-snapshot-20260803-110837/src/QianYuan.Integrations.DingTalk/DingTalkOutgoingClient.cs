using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace QianYuan.Integrations.DingTalk;

/// <summary>
/// Outgoing client for DingTalk custom-robot webhooks. Supports timestamp+sign signing
/// and updating an in-flight message (used to fake streaming via card refreshes).
/// </summary>
public sealed class DingTalkOutgoingClient
{
    private readonly HttpClient _http;
    private readonly DingTalkOptions _opts;
    private readonly ILogger<DingTalkOutgoingClient> _logger;

    public DingTalkOutgoingClient(HttpClient http, IOptions<DingTalkOptions> opts, ILogger<DingTalkOutgoingClient> logger)
    {
        _http = http;
        _opts = opts.Value;
        _logger = logger;
    }

    /// <summary>Send a markdown message via the custom-robot webhook.</summary>
    public async Task SendMarkdownAsync(string title, string markdown, IReadOnlyList<string>? atMobiles = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_opts.OutgoingWebhookUrl))
            throw new InvalidOperationException("DingTalk outgoing webhook URL not configured.");

        var url = SignedUrl(_opts.OutgoingWebhookUrl, _opts.OutgoingSecret);
        var body = new
        {
            msgtype = "markdown",
            markdown = new { title, text = markdown },
            at = new { atMobiles = atMobiles ?? Array.Empty<string>(), isAtAll = false }
        };
        using var resp = await _http.PostAsJsonAsync(url, body, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogError("DingTalk send failed: {Status} {Body}", resp.StatusCode, err);
        }
    }

    /// <summary>Send a plain-text message.</summary>
    public async Task SendTextAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_opts.OutgoingWebhookUrl))
            throw new InvalidOperationException("DingTalk outgoing webhook URL not configured.");
        var url = SignedUrl(_opts.OutgoingWebhookUrl, _opts.OutgoingSecret);
        var body = new { msgtype = "text", text = new { content = text } };
        using var resp = await _http.PostAsJsonAsync(url, body, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>Sign a custom-robot webhook URL with timestamp+sign when secret is configured.</summary>
    public static string SignedUrl(string baseUrl, string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return baseUrl;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var stringToSign = $"{ts}\n{secret}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        var sign = Uri.EscapeDataString(Convert.ToBase64String(hash));
        var sep = baseUrl.Contains('?') ? '&' : '?';
        return $"{baseUrl}{sep}timestamp={ts}&sign={sign}";
    }
}
