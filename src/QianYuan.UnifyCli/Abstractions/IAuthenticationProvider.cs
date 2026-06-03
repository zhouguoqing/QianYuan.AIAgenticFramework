namespace QianYuan.UnifyCli.Abstractions;

/// <summary>
/// Defines authentication providers for HTTP requests to external services.
/// Implementations handle different auth schemes (Basic, Bearer, API Key, OAuth2, etc.)
/// </summary>
public interface IAuthenticationProvider
{
    /// <summary>
    /// Applies authentication to the HTTP request message.
    /// </summary>
    /// <param name="request">The HTTP request message to authenticate.</param>
    /// <returns>A task that completes when authentication is applied.</returns>
    Task ApplyAsync(HttpRequestMessage request);

    /// <summary>
    /// Gets a human-readable description of the authentication method.
    /// </summary>
    string Description { get; }
}

/// <summary>
/// Factory for creating authentication providers based on configuration.
/// </summary>
public interface IAuthenticationProviderFactory
{
    /// <summary>
    /// Creates an authentication provider from the given options.
    /// </summary>
    /// <param name="options">Authentication options.</param>
    /// <returns>An authentication provider instance.</returns>
    IAuthenticationProvider Create(AuthenticationOptions options);
}

/// <summary>
/// Configuration for authentication methods.
/// </summary>
public sealed class AuthenticationOptions
{
    /// <summary>Authentication method type: "basic", "bearer", "api_key", "oauth2", "custom_header", "none".</summary>
    public required string Type { get; init; }

    /// <summary>Username for Basic Auth.</summary>
    public string? Username { get; init; }

    /// <summary>Password for Basic Auth.</summary>
    public string? Password { get; init; }

    /// <summary>Token value for Bearer or API Key auth.</summary>
    public string? Token { get; init; }

    /// <summary>Header name for API Key (e.g., "X-API-Key", "Authorization").</summary>
    public string? HeaderName { get; init; }

    /// <summary>Query parameter name for API Key (e.g., "api_key").</summary>
    public string? QueryParamName { get; init; }

    /// <summary>Custom headers to add to every request.</summary>
    public IReadOnlyDictionary<string, string>? CustomHeaders { get; init; }

    /// <summary>Additional metadata for extensibility.</summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
}
