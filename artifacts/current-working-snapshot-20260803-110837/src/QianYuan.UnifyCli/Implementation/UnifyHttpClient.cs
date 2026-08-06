using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using QianYuan.UnifyCli.Abstractions;

namespace QianYuan.UnifyCli.Implementation;

/// <summary>
/// HTTP service client for executing CLI methods that call external HTTPS services.
/// Handles authentication, request building, response transformation, and retries.
/// </summary>
public sealed class UnifyHttpClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IAuthenticationProviderFactory _authFactory;

    public UnifyHttpClient(HttpClient? httpClient = null, IAuthenticationProviderFactory? authFactory = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _authFactory = authFactory ?? new AuthenticationProviderFactory();
    }

    /// <summary>
    /// Executes a CLI method by making the underlying HTTP request.
    /// </summary>
    public async ValueTask<CliInvocationResult> ExecuteAsync(
        ICliMethod method,
        string parametersJson,
        IAuthenticationProvider? defaultAuth = null,
        CancellationToken ct = default)
    {
        var startTime = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var parameters = JsonNode.Parse(string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson) ?? new JsonObject();

            // Build the URL
            var url = BuildUrl(method, parameters);

            // Create the request
            var request = new HttpRequestMessage
            {
                Method = new HttpMethod(method.HttpMethod),
                RequestUri = new Uri(url)
            };

            // Add request headers
            if (method.RequestHeaders != null)
            {
                foreach (var header in method.RequestHeaders)
                {
                    if (!header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) &&
                        !header.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase))
                    {
                        request.Headers.Add(header.Key, header.Value);
                    }
                    else if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        request.Content?.Headers.Remove("Content-Type");
                    }
                }
            }

            // Add request body if applicable
            if (!string.IsNullOrEmpty(method.RequestBodyTemplate) &&
                (method.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
                 method.HttpMethod.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
                 method.HttpMethod.Equals("PATCH", StringComparison.OrdinalIgnoreCase)))
            {
                var body = InterpolateTemplate(method.RequestBodyTemplate, parameters);
                request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            }

            // Apply authentication
            var auth = method.AuthenticationProvider ?? defaultAuth;
            if (auth != null)
            {
                await auth.ApplyAsync(request).ConfigureAwait(false);
            }

            // Execute with retries
            return await ExecuteWithRetryAsync(request, method, startTime, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            startTime.Stop();
            return CliInvocationResult.Error(ex.Message, executionTimeMs: startTime.ElapsedMilliseconds);
        }
    }

    private string BuildUrl(ICliMethod method, JsonNode parameters)
    {
        var baseUri = method.BaseUri.TrimEnd('/');
        var path = method.PathTemplate;

        // Interpolate path parameters
        path = InterpolatePathParameters(path, parameters);

        var url = $"{baseUri}{path}";

        // Add query parameters
        var queryParams = new List<string>();
        if (method.QueryParams != null)
        {
            foreach (var param in method.QueryParams)
            {
                var value = param.Value.StartsWith("$")
                    ? parameters[param.Value.Substring(1)]?.GetValue<string>() ?? ""
                    : param.Value;
                queryParams.Add($"{Uri.EscapeDataString(param.Key)}={Uri.EscapeDataString(value)}");
            }
        }

        if (queryParams.Count > 0)
        {
            url += "?" + string.Join("&", queryParams);
        }

        return url;
    }

    private string InterpolatePathParameters(string pathTemplate, JsonNode parameters)
    {
        var result = pathTemplate;
        var paramMatches = Regex.Matches(pathTemplate, @"\{(\w+)\}");
        foreach (Match match in paramMatches)
        {
            var paramName = match.Groups[1].Value;
            var value = parameters[paramName]?.GetValue<string>() ?? "";
            result = result.Replace($"{{{paramName}}}", Uri.EscapeDataString(value));
        }
        return result;
    }

    private string InterpolateTemplate(string template, JsonNode parameters)
    {
        if (template.StartsWith("$"))
        {
            // Reference a parameter
            var paramName = template.Substring(1);
            var value = parameters[paramName];
            return value?.ToJsonString() ?? "{}";
        }
        else
        {
            // Treat as JSON template - could implement Handlebars-like templating here if needed
            return template;
        }
    }

    private async ValueTask<CliInvocationResult> ExecuteWithRetryAsync(
        HttpRequestMessage request,
        ICliMethod method,
        System.Diagnostics.Stopwatch startTime,
        CancellationToken ct)
    {
        int attempt = 0;
        int maxAttempts = method.RetryCount + 1;
        Exception? lastException = null;

        while (attempt < maxAttempts)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(method.TimeoutMs);

                var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                startTime.Stop();

                // Transform response if needed
                if (method.ResponseTransformer != null)
                {
                    var headers = response.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value));
                    content = await method.ResponseTransformer.TransformAsync((int)response.StatusCode, content, headers).ConfigureAwait(false);
                }

                if (response.IsSuccessStatusCode)
                {
                    return CliInvocationResult.Ok(content, content, (int)response.StatusCode, startTime.ElapsedMilliseconds);
                }
                else
                {
                    return CliInvocationResult.Error($"HTTP {(int)response.StatusCode}: {content}", (int)response.StatusCode, startTime.ElapsedMilliseconds);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lastException = new TimeoutException($"Request timeout after {method.TimeoutMs}ms");
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
            }

            attempt++;
            if (attempt < maxAttempts && lastException != null)
            {
                await Task.Delay(method.RetryDelayMs, ct).ConfigureAwait(false);
            }
        }

        startTime.Stop();
        return CliInvocationResult.Error(
            lastException?.Message ?? "Request failed after retries",
            executionTimeMs: startTime.ElapsedMilliseconds);
    }

    public void Dispose() => _httpClient?.Dispose();
}
