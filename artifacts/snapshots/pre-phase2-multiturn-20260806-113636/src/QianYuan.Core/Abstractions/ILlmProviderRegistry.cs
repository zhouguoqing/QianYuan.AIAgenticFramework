namespace QianYuan.Core.Abstractions;

/// <summary>
/// Registry of LLM providers. Allows the kernel to resolve a provider by id at run time.
/// </summary>
public interface ILlmProviderRegistry
{
    /// <summary>Register a provider. Throws if id already taken.</summary>
    void Register(ILlmProvider provider);

    /// <summary>Resolve by id. Returns null if not found.</summary>
    ILlmProvider? Get(string providerId);

    /// <summary>Default provider used when no override supplied.</summary>
    ILlmProvider Default { get; }

    /// <summary>All providers in registration order.</summary>
    IReadOnlyList<ILlmProvider> List();
}
