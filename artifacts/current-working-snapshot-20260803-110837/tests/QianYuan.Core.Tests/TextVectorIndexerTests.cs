using FluentAssertions;
using QianYuan.Api.Services;

namespace QianYuan.Core.Tests;

public class TextVectorIndexerTests
{
    [Fact]
    public void Vectorize_produces_consistent_output()
    {
        var indexer = new TextVectorIndexer(128, 32);
        var text = "hello world machine learning";

        var vec1 = indexer.Vectorize(text);
        var vec2 = indexer.Vectorize(text);

        vec1.Should().BeEquivalentTo(vec2);
    }

    [Fact]
    public void Vectorize_produces_correct_dimension()
    {
        var indexer = new TextVectorIndexer(128, 32);
        var text = "test document content";

        var vector = indexer.Vectorize(text);

        vector.Length.Should().Be(128);
    }

    [Fact]
    public void Vectorize_ignores_case()
    {
        var indexer = new TextVectorIndexer(128, 32);
        var lower = indexer.Vectorize("hello world");
        var upper = indexer.Vectorize("HELLO WORLD");
        var mixed = indexer.Vectorize("Hello World");

        lower.Should().BeEquivalentTo(upper);
        lower.Should().BeEquivalentTo(mixed);
    }

    [Fact]
    public void Normalize_produces_unit_length_vector()
    {
        var indexer = new TextVectorIndexer(128, 32);
        var vector = indexer.Vectorize("test content");
        var normalized = indexer.Normalize(vector);

        var magnitude = Math.Sqrt(normalized.Sum(v => v * v));
        magnitude.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void Normalize_handles_zero_vector()
    {
        var indexer = new TextVectorIndexer(128, 32);
        var zero = new double[128];

        var result = indexer.Normalize(zero);

        result.Should().BeEquivalentTo(zero);
    }

    [Fact]
    public void Add_and_Search_find_similar_vectors()
    {
        var indexer = new TextVectorIndexer(128, 32);
        var doc1 = "machine learning artificial intelligence";
        var doc2 = "deep neural networks";
        var doc3 = "python programming language";

        var vec1 = indexer.Normalize(indexer.Vectorize(doc1));
        var vec2 = indexer.Normalize(indexer.Vectorize(doc2));
        var vec3 = indexer.Normalize(indexer.Vectorize(doc3));

        indexer.Add("doc1", vec1);
        indexer.Add("doc2", vec2);
        indexer.Add("doc3", vec3);

        var query = indexer.Normalize(indexer.Vectorize("machine learning"));
        var results = indexer.Search(query, topK: 2);

        results.Length.Should().Be(2);
        results.Should().Contain("doc1");
    }

    [Fact]
    public void Search_returns_empty_when_no_documents()
    {
        var indexer = new TextVectorIndexer(128, 32);
        var query = indexer.Normalize(indexer.Vectorize("test"));

        var results = indexer.Search(query, topK: 5);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Rebuild_clears_and_repopulates()
    {
        var indexer = new TextVectorIndexer(128, 32);
        var docs = new[]
        {
            new KnowledgeDocument { Id = "1", Title = "Doc 1", Content = "content one", Vector = indexer.Normalize(indexer.Vectorize("content one")) },
            new KnowledgeDocument { Id = "2", Title = "Doc 2", Content = "content two", Vector = indexer.Normalize(indexer.Vectorize("content two")) },
        };

        foreach (var doc in docs)
        {
            indexer.Add(doc.Id, doc.Vector);
        }

        var query = indexer.Normalize(indexer.Vectorize("content"));
        var resultsBefore = indexer.Search(query, topK: 5);

        indexer.Rebuild(docs);
        var resultsAfter = indexer.Search(query, topK: 5);

        resultsAfter.Should().HaveCount(resultsBefore.Length);
    }

    [Fact]
    public void Different_texts_produce_different_vectors()
    {
        var indexer = new TextVectorIndexer(128, 32);
        var vec1 = indexer.Vectorize("machine learning");
        var vec2 = indexer.Vectorize("cooking recipe");

        vec1.SequenceEqual(vec2).Should().BeFalse();
    }

    [Fact]
    public void Empty_text_produces_zero_vector()
    {
        var indexer = new TextVectorIndexer(128, 32);
        var vector = indexer.Vectorize("");

        vector.All(v => v == 0).Should().BeTrue();
    }

    [Fact]
    public void Search_respects_topK_parameter()
    {
        var indexer = new TextVectorIndexer(64, 16);
        for (var i = 0; i < 20; i++)
        {
            var vec = indexer.Normalize(indexer.Vectorize($"document {i}"));
            indexer.Add($"doc{i}", vec);
        }

        var query = indexer.Normalize(indexer.Vectorize("document"));
        var results3 = indexer.Search(query, topK: 3);
        var results5 = indexer.Search(query, topK: 5);

        results3.Length.Should().BeLessThanOrEqualTo(3);
        results5.Length.Should().BeLessThanOrEqualTo(5);
        results5.Length.Should().BeGreaterThanOrEqualTo(results3.Length);
    }
}
