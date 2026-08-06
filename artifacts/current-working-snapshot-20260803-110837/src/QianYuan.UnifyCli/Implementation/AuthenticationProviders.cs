using System.Text;
using QianYuan.UnifyCli.Abstractions;

namespace QianYuan.UnifyCli.Implementation;

/// <summary>
/// No authentication - used when the service doesn't require auth.
/// </summary>
internal sealed class NoAuthenticationProvider : IAuthenticationProvider
{
    public string Description => "No authentication";

    public Task ApplyAsync(HttpRequestMessage request) => Task.CompletedTask;
}

/// <summary>
/// Basic authentication using username and password.
/// </summary>
internal sealed class BasicAuthenticationProvider : IAuthenticationProvider
{
    private readonly string _encodedCredentials;

    public string Description => "HTTP Basic Authentication";

    public BasicAuthenticationProvider(string username, string password)
    {
        var credentials = $"{username}:{password}";
        var bytes = Encoding.UTF8.GetBytes(credentials);
        _encodedCredentials = Convert.ToBase64String(bytes);
    }

    public Task ApplyAsync(HttpRequestMessage request)
    {
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", _encodedCredentials);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Bearer token authentication (e.g., JWT, OAuth2 access token).
/// </summary>
internal sealed class BearerAuthenticationProvider : IAuthenticationProvider
{
    private readonly string _token;

    public string Description => "Bearer Token";

    public BearerAuthenticationProvider(string token)
    {
        _token = token;
    }

    public Task ApplyAsync(HttpRequestMessage request)
    {
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
        return Task.CompletedTask;
    }
}

/// <summary>
/// API Key authentication - header or query parameter based.
/// </summary>
internal sealed class ApiKeyAuthenticationProvider : IAuthenticationProvider
{
    private readonly string _token;
    private readonly string? _headerName;
    private readonly string? _queryParamName;

    public string Description => $"API Key ({(_headerName ?? _queryParamName ?? "unknown")})";

    public ApiKeyAuthenticationProvider(string token, string? headerName = null, string? queryParamName = null)
    {
        _token = token;
        _headerName = headerName;
        _queryParamName = queryParamName;
    }

    public Task ApplyAsync(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_headerName))
        {
            request.Headers.Add(_headerName, _token);
        }
        else if (!string.IsNullOrEmpty(_queryParamName))
        {
            var separator = request.RequestUri?.Query.Length > 0 ? "&" : "?";
            var originalUri = request.RequestUri?.ToString() ?? "";
            var newUri = $"{originalUri}{separator}{Uri.EscapeDataString(_queryParamName)}={Uri.EscapeDataString(_token)}";
            request.RequestUri = new Uri(newUri);
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Custom header authentication - adds arbitrary headers to requests.
/// </summary>
internal sealed class CustomHeaderAuthenticationProvider : IAuthenticationProvider
{
    private readonly IReadOnlyDictionary<string, string> _headers;

    public string Description => "Custom Headers";

    public CustomHeaderAuthenticationProvider(IReadOnlyDictionary<string, string> headers)
    {
        _headers = headers ?? new Dictionary<string, string>();
    }

    public Task ApplyAsync(HttpRequestMessage request)
    {
        foreach (var header in _headers)
        {
            request.Headers.Add(header.Key, header.Value);
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Factory for creating authentication providers based on options.
/// </summary>
public sealed class AuthenticationProviderFactory : IAuthenticationProviderFactory
{
    public IAuthenticationProvider Create(AuthenticationOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        return options.Type.ToLowerInvariant() switch
        {
            "none" => new NoAuthenticationProvider(),
            "basic" => new BasicAuthenticationProvider(options.Username ?? "", options.Password ?? ""),
            "bearer" => new BearerAuthenticationProvider(options.Token ?? ""),
            "api_key" => new ApiKeyAuthenticationProvider(options.Token ?? "", options.HeaderName, options.QueryParamName),
            "custom_header" => new CustomHeaderAuthenticationProvider(options.CustomHeaders ?? new Dictionary<string, string>()),
            _ => throw new ArgumentException($"Unknown authentication type: {options.Type}")
        };
    }
}
