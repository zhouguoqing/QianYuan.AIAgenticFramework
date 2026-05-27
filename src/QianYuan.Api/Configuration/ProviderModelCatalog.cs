using Microsoft.Extensions.Options;

namespace QianYuan.Api.Configuration;

/// <summary>
/// Maps a configured provider id to the list of model ids the UI may offer. Sourced from the
/// `Models` array in each provider section of QianYuan options, with the provider's DefaultModel
/// (or DefaultDeployment for Azure) prepended if it isn't already present.
/// </summary>
public sealed class ProviderModelCatalog
{
    private readonly Dictionary<string, IReadOnlyList<string>> _byProvider;
    private readonly string? _defaultProviderId;
    private readonly Dictionary<string, string> _defaultModelByProvider;

    public ProviderModelCatalog(IOptions<QianYuanApiOptions> options)
    {
        var qy = options.Value;
        _defaultProviderId = qy.DefaultProviderId;
        _byProvider = new(StringComparer.OrdinalIgnoreCase);
        _defaultModelByProvider = new(StringComparer.OrdinalIgnoreCase);

        foreach (var p in qy.OpenAICompatProviders)
            Add(p.ProviderId, p.DefaultModel, p.Models);

        foreach (var a in qy.AzureOpenAIProviders)
        {
            var extra = a.Models.Concat(a.ModelToDeployment.Keys).ToArray();
            Add(a.ProviderId, a.DefaultDeployment, extra);
        }

        if (qy.Anthropic is { } ant) Add(ant.ProviderId, ant.DefaultModel, ant.Models);
        if (qy.Gemini is { } gem) Add(gem.ProviderId, gem.DefaultModel, gem.Models);
        if (qy.Qwen is { } qw) Add(qw.ProviderId, qw.DefaultModel, qw.Models);
    }

    public IReadOnlyList<string> ModelsFor(string providerId)
        => _byProvider.TryGetValue(providerId, out var list) ? list : Array.Empty<string>();

    public string? DefaultProviderId => _defaultProviderId;

    public string? DefaultModelFor(string providerId)
        => _defaultModelByProvider.TryGetValue(providerId, out var m) ? m : null;

    private void Add(string providerId, string defaultModel, IEnumerable<string> extra)
    {
        if (string.IsNullOrEmpty(providerId)) return;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        if (!string.IsNullOrWhiteSpace(defaultModel) && seen.Add(defaultModel))
            ordered.Add(defaultModel);
        foreach (var m in extra)
            if (!string.IsNullOrWhiteSpace(m) && seen.Add(m)) ordered.Add(m);

        _byProvider[providerId] = ordered;
        if (!string.IsNullOrWhiteSpace(defaultModel))
            _defaultModelByProvider[providerId] = defaultModel;
    }
}
