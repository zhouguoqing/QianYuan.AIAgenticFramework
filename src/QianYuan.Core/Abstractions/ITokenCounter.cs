namespace QianYuan.Core.Abstractions;

/// <summary>
/// Estimates token usage for context-window management. Implementations may use
/// model-specific tokenizers; the default kernel implementation is conservative
/// and dependency-free.
/// </summary>
public interface ITokenCounter
{
    int CountText(string? text, string? model = null);
}
