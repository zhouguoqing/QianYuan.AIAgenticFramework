using QianYuan.Core.Models;

namespace QianYuan.Api.Services;

public sealed class KnowledgeDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string SourceFile { get; set; } = string.Empty;
    public string SourceSection { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [System.Text.Json.Serialization.JsonIgnore]
    internal double[]? Vector { get; set; }
}

public interface IKnowledgeStore
{
    Task<KnowledgeDocument> AddAsync(KnowledgeDocument doc, CancellationToken ct = default);
    Task<IEnumerable<KnowledgeDocument>> ListAsync(CancellationToken ct = default);
    Task<KnowledgeDocument?> GetAsync(string id, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Search by simple keyword matching and optionally ask the provider to synthesize an answer using the top matches.
    /// Returns (matching documents, optional generated answer text).
    /// </summary>
    Task<(IEnumerable<KnowledgeDocument> matches, string? answer)> SearchAsync(string q, int topK = 5, bool answer = false, string? providerId = null, CancellationToken ct = default);
}
