using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace QianYuan.Api.Services;

public sealed class TextVectorIndexer
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
