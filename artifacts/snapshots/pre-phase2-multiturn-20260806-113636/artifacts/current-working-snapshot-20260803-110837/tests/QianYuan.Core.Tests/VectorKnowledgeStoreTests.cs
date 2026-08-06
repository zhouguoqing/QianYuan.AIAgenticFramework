using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using QianYuan.Api.Services;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;

namespace QianYuan.Core.Tests;

public class VectorKnowledgeStoreTests
{
    private readonly VectorKnowledgeStore _store;
    private readonly Mock<ILlmProviderRegistry> _providerRegistry;

    public VectorKnowledgeStoreTests()
    {
        _providerRegistry = new Mock<ILlmProviderRegistry>();
        _store = new VectorKnowledgeStore(_providerRegistry.Object);
    }

    [Fact]
    public async Task AddAsync_adds_document_and_computes_vector()
    {
        var doc = new KnowledgeDocument
        {
            Title = "Test Document",
            Content = "This is test content for vector computation",
        };

        var result = await _store.AddAsync(doc);

        result.Should().NotBeNull();
        ((object)result.Vector).Should().NotBeNull();
        result.Vector!.Length.Should().Be(128);
    }

    [Fact]
    public async Task ListAsync_returns_documents_ordered_by_created_date_descending()
    {
        var doc1 = new KnowledgeDocument { Title = "Doc 1", Content = "Content 1" };
        var doc2 = new KnowledgeDocument { Title = "Doc 2", Content = "Content 2" };

        await _store.AddAsync(doc1);
        await Task.Delay(50);
        await _store.AddAsync(doc2);

        var result = (await _store.ListAsync()).ToList();

        result.Should().HaveCount(2);
        result[0].Id.Should().Be(doc2.Id);
        result[1].Id.Should().Be(doc1.Id);
    }

    [Fact]
    public async Task GetAsync_returns_existing_document()
    {
        var doc = new KnowledgeDocument { Title = "Test Doc", Content = "Test content" };
        await _store.AddAsync(doc);

        var result = await _store.GetAsync(doc.Id);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Test Doc");
        result.Id.Should().Be(doc.Id);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_missing_document()
    {
        var result = await _store.GetAsync("missing-id");

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_removes_document()
    {
        var doc = new KnowledgeDocument { Title = "To Delete", Content = "Content" };
        await _store.AddAsync(doc);

        var deleted = await _store.DeleteAsync(doc.Id);
        var retrieved = await _store.GetAsync(doc.Id);

        deleted.Should().BeTrue();
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_returns_false_for_missing_document()
    {
        var result = await _store.DeleteAsync("missing-id");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_returns_empty_when_query_is_empty()
    {
        var result = await _store.SearchAsync("");

        result.matches.Should().BeEmpty();
        result.answer.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_finds_documents_by_vector_similarity()
    {
        var doc1 = new KnowledgeDocument
        {
            Title = "Machine Learning Basics",
            Content = "Machine learning is a subset of artificial intelligence focused on learning from data",
            Tags = new[] { "ml", "ai" }
        };
        var doc2 = new KnowledgeDocument
        {
            Title = "Deep Learning",
            Content = "Deep learning uses neural networks to process data",
            Tags = new[] { "deep-learning", "ai" }
        };
        var doc3 = new KnowledgeDocument
        {
            Title = "Python Programming",
            Content = "Python is a popular programming language",
            Tags = new[] { "python" }
        };

        await _store.AddAsync(doc1);
        await _store.AddAsync(doc2);
        await _store.AddAsync(doc3);

        var result = await _store.SearchAsync("artificial intelligence", topK: 2);

        result.matches.Should().NotBeEmpty();
        result.matches.Should().HaveCountLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task SearchAsync_supports_multiple_documents_with_tags()
    {
        var docs = new[]
        {
            new KnowledgeDocument { Title = "Doc A", Content = "content a", Tags = new[] { "tag1" } },
            new KnowledgeDocument { Title = "Doc B", Content = "content b", Tags = new[] { "tag2" } },
            new KnowledgeDocument { Title = "Doc C", Content = "content c", Tags = new[] { "tag1", "tag3" } },
        };

        foreach (var doc in docs)
        {
            await _store.AddAsync(doc);
        }

        var allDocs = await _store.ListAsync();
        allDocs.Should().HaveCount(3);
    }

    [Fact]
    public async Task SearchAsync_topK_parameter_limits_results()
    {
        var docs = Enumerable.Range(1, 10)
            .Select(i => new KnowledgeDocument { Title = $"Doc {i}", Content = $"This is document number {i}" })
            .ToList();

        foreach (var doc in docs)
        {
            await _store.AddAsync(doc);
        }

        var result = await _store.SearchAsync("document", topK: 3);

        result.matches.Count().Should().BeLessThanOrEqualTo(3);
    }
}
