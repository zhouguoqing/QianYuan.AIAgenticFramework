using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        var tokens = Tokenize(q);
        var content = string.Join(' ', new[] { d.Title, d.Content, string.Join(' ', d.Tags) }).ToLowerInvariant();
        return tokens.Sum(t => Regex.Matches(content, Regex.Escape(t)).Count);
    }

    private static IReadOnlyList<string> Tokenize(string text)
        => Regex.Split(text ?? string.Empty, "\\W+").Where(x => x.Length > 1).Select(x => x.ToLowerInvariant()).ToArray();

    private sealed class TextVectorIndexer
    {
        private readonly int _dimension;
        private readonly int _hashPlanes;
        private readonly List<string> _ids = new();
        private readonly List<double[]> _vectors = new();
        private readonly Dictionary<ulong, List<int>> _buckets = new();
        private readonly double[][] _hyperplanes;

        public TextVectorIndexer(int dimension, int hashPlanes)
        {
            _dimension = dimension;
            _hashPlanes = Math.Min(hashPlanes, 64);
            _hyperplanes = CreateHyperplanes(_dimension, _hashPlanes);
        }

        public void Add(string id, double[] vector)
        {
            _ids.Add(id);
            _vectors.Add(vector);
            var bucket = ComputeBucket(vector);
            if (!_buckets.TryGetValue(bucket, out var hits))
            {
                hits = new List<int>();
                _buckets[bucket] = hits;
            }
            hits.Add(_ids.Count - 1);
        }

        public void Rebuild(IEnumerable<KnowledgeDocument> docs)
        {
            _ids.Clear();
            _vectors.Clear();
            _buckets.Clear();
            foreach (var doc in docs)
            {
                if (doc.Vector is { Length: > 0 }) Add(doc.Id, doc.Vector);
            }
        }

        public string[] Search(double[] query, int topK)
        {
            if (_ids.Count == 0) return Array.Empty<string>();
            var bucket = ComputeBucket(query);
            var candidates = new HashSet<int>();
            if (_buckets.TryGetValue(bucket, out var hits)) candidates.UnionWith(hits);
            if (candidates.Count < topK * 3)
            {
                candidates.UnionWith(Enumerable.Range(0, _ids.Count));
            }

            return candidates
                .Select(i => new { Id = _ids[i], Score = Dot(query, _vectors[i]) })
                .OrderByDescending(x => x.Score)
                .Take(topK)
                .Select(x => x.Id)
                .ToArray();
        }

        public double[] Vectorize(string text)
        {
            var vector = new double[_dimension];
            foreach (var token in Regex.Split(text.ToLowerInvariant(), "\\W+").Where(x => x.Length > 1))
            {
                var hash = HashToken(token);
                var index = (int)(hash % (uint)_dimension);
                vector[index] += 1.0;
            }
            for (var i = 0; i < vector.Length; i++) vector[i] = Math.Log(1.0 + vector[i]);
            return vector;
        }

        public double[] Normalize(double[] vector)
        {
            var norm = Math.Sqrt(vector.Sum(v => v * v));
            if (norm <= 1e-9) return vector;
            for (var i = 0; i < vector.Length; i++) vector[i] /= norm;
            return vector;
        }

        private ulong ComputeBucket(double[] vector)
        {
            ulong bucket = 0;
            for (var i = 0; i < _hashPlanes; i++)
            {
                if (Dot(vector, _hyperplanes[i]) >= 0) bucket |= 1UL << i;
            }
            return bucket;
        }

        private static double Dot(double[] a, double[] b)
        {
            var sum = 0.0;
            for (var i = 0; i < a.Length; i++) sum += a[i] * b[i];
            return sum;
        }

        private static uint HashToken(string token)
        {
            const uint fnvOffsetBasis = 2166136261;
            const uint fnvPrime = 16777619;
            var hash = fnvOffsetBasis;
            foreach (var c in token)
            {
                hash ^= c;
                hash *= fnvPrime;
            }
            return hash;
        }

        private static double[][] CreateHyperplanes(int dimension, int count)
        {
            var random = new Random(123456);
            var planes = new double[count][];
            for (var i = 0; i < count; i++)
            {
                planes[i] = new double[dimension];
                for (var j = 0; j < dimension; j++)
                {
                    planes[i][j] = random.NextDouble() * 2.0 - 1.0;
                }
            }
            return planes;
        }
    }
}
