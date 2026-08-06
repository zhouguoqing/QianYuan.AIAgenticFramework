using QianYuan.UnifyCli.Abstractions;

namespace QianYuan.UnifyCli.Implementation;

/// <summary>
/// Builder pattern helper for creating CLI service definitions.
/// </summary>
public sealed class CliServiceBuilder
{
    private readonly CliServiceDefinition _service;

    public CliServiceBuilder(string id, string name, string baseUri)
    {
        _service = new CliServiceDefinition
        {
            Id = id,
            Name = name,
            BaseUri = baseUri
        };
    }

    public CliServiceBuilder WithDescription(string description)
    {
        _service.Description = description;
        return this;
    }

    public CliServiceBuilder WithDefaultAuth(IAuthenticationProvider auth)
    {
        _service.DefaultAuthenticationProvider = auth;
        return this;
    }

    public CliServiceBuilder WithTags(params string[] tags)
    {
        _service.Tags = tags;
        return this;
    }

    public CliServiceBuilder AddMethod(ICliMethod method)
    {
        _service.RegisterMethod(method);
        return this;
    }

    public CliServiceDefinition Build() => _service;
}

/// <summary>
/// Base implementation of a CLI service that manages multiple related CLI methods.
/// </summary>
public sealed class CliServiceDefinition : ICliService
{
    private readonly Dictionary<string, ICliMethod> _methods;
    private readonly UnifyHttpClient _httpClient;

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string BaseUri { get; set; } = "";
    public IAuthenticationProvider? DefaultAuthenticationProvider { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();

    public CliServiceDefinition()
    {
        _methods = new Dictionary<string, ICliMethod>(StringComparer.OrdinalIgnoreCase);
        _httpClient = new UnifyHttpClient();
    }

    /// <summary>
    /// Registers a CLI method in this service.
    /// </summary>
    public void RegisterMethod(ICliMethod method)
    {
        if (method == null) throw new ArgumentNullException(nameof(method));
        _methods[method.Id] = method;
    }

    /// <summary>
    /// Registers multiple CLI methods in this service.
    /// </summary>
    public void RegisterMethods(params ICliMethod[] methods)
    {
        foreach (var method in methods)
        {
            RegisterMethod(method);
        }
    }

    public ValueTask<IReadOnlyList<ICliMethod>> GetMethodsAsync(CancellationToken ct = default)
        => new ValueTask<IReadOnlyList<ICliMethod>>(_methods.Values.ToArray());

    public ValueTask<ICliMethod?> GetMethodAsync(string methodId, CancellationToken ct = default)
    {
        _methods.TryGetValue(methodId, out var method);
        return new ValueTask<ICliMethod?>(method);
    }

    public async ValueTask<CliInvocationResult> InvokeAsync(string methodId, string parametersJson, CancellationToken ct = default)
    {
        var method = await GetMethodAsync(methodId, ct).ConfigureAwait(false);
        if (method == null)
        {
            return CliInvocationResult.Error($"Method '{methodId}' not found in service '{Id}'");
        }

        return await _httpClient.ExecuteAsync(method, parametersJson, DefaultAuthenticationProvider, ct).ConfigureAwait(false);
    }

    public void Dispose() => _httpClient?.Dispose();
}
