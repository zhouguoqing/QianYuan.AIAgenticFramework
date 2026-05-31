using System.Linq;
using System.Text;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;

namespace QianYuan.Api.Services;

public sealed class VectorKnowledgeStore : IKnowledgeStore
{
    private readonly List<KnowledgeDocument> _docs = new();
    private readonly TextVectorIndexer _index = new(128, 32);
    private readonly ILlmProviderRegistry _providers;
    private readonly object _lock = new();

    public VectorKnowledgeStore(ILlmProviderRegistry providers)
    {
        _providers = providers;
    }

    public Task<KnowledgeDocument> AddAsync(KnowledgeDocument doc, CancellationToken ct = default)
    {
        doc.Vector = _index.Normalize(_index.Vectorize(doc.Content));
        doc.CreatedAt = DateTime.UtcNow;
        lock (_lock)
        {
            _docs.Add(doc);
            _index.Add(doc.Id, doc.Vector);
        }

        return Task.FromResult(doc);
    }

    public Task<IEnumerable<KnowledgeDocument>> ListAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_docs.OrderByDescending(d => d.CreatedAt).AsEnumerable());
        }
    }

    public Task<KnowledgeDocument?> GetAsync(string id, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(_docs.FirstOrDefault(d => d.Id == id)); }
    }

    public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var doc = _docs.FirstOrDefault(x => x.Id == id);
            if (doc is null) return Task.FromResult(false);
            _docs.Remove(doc);
            _index.Rebuild(_docs);
            return Task.FromResult(true);
        }
    }

    public async Task<(IEnumerable<KnowledgeDocument> matches, string? answer)> SearchAsync(string q, int topK = 5, bool answer = false, string? providerId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q)) return (Array.Empty<KnowledgeDocument>(), null);

        KnowledgeDocument[] candidates;
        var queryVector = _index.Normalize(_index.Vectorize(q));

        lock (_lock)
        {
            var candidateIds = _docs.Count == 0 ? Array.Empty<string>() : _index.Search(queryVector, Math.Max(topK, 5));
            candidates = candidateIds
                .Select(id => _docs.FirstOrDefault(d => d.Id == id))
                .Where(d => d is not null)
                .Cast<KnowledgeDocument>()
                .Take(topK)
                .ToArray();

            if (candidates.Length == 0)
            {
                candidates = _docs.Select(d => new { Doc = d, Score = ScoreByKeyword(q, d) })
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.Doc.CreatedAt)
                    .Select(x => x.Doc)
                    .Take(topK)
                    .ToArray();
            }
        }

        if (!answer) return (candidates, null);

        var provider = providerId is null ? _providers.Default : _providers.Get(providerId) ?? _providers.Default;
        var docsText = new StringBuilder();
        foreach (var d in candidates)
        {
            docsText.AppendLine($"Title: {d.Title}");
            if (!string.IsNullOrEmpty(d.SourceFile)) docsText.AppendLine($"Source: {d.SourceFile} {d.SourceSection}".Trim());
            docsText.AppendLine(d.Content);
            docsText.AppendLine("---");
        }

        var system = ChatMessage.System("You are a knowledge assistant. Answer concisely using only the provided documents. If the answer is not present, say you don't know and list what additional information is needed.");
        var user = ChatMessage.User($"Context:\n{docsText}\n\nQuestion: {q}");
        var req = new ChatRequest { Messages = new[] { system, user } };

        try
        {
            var resp = await provider.CompleteAsync(req, ct).ConfigureAwait(false);
            return (candidates, resp.Message.AsPlainText());
        }
        catch
        {
            return (candidates, null);
        }
    }

    private static int ScoreByKeyword(string q, KnowledgeDocument d)
    {
        return KnowledgeSearch.ScoreByKeyword(q, d);
    }
}

