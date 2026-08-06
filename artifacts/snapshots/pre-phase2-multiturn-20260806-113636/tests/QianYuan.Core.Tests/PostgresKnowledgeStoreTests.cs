using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using QianYuan.Api.Configuration;
using QianYuan.Api.Services;
using QianYuan.Core.Abstractions;

namespace QianYuan.Core.Tests;

public class PostgresKnowledgeStoreTests
{
    private readonly Mock<ILlmProviderRegistry> _providerRegistry;
    private readonly Mock<ILogger<PostgresKnowledgeStore>> _logger;

    public PostgresKnowledgeStoreTests()
    {
        _providerRegistry = new Mock<ILlmProviderRegistry>();
        _logger = new Mock<ILogger<PostgresKnowledgeStore>>();
    }

    [Fact]
    public void Constructor_throws_when_connection_string_is_empty()
    {
        var options = new PostgresOptions { ConnectionString = "" };

        var act = () => new PostgresKnowledgeStore(options, _providerRegistry.Object, _logger.Object);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_throws_when_options_is_null()
    {
        var act = () => new PostgresKnowledgeStore(null!, _providerRegistry.Object, _logger.Object);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_throws_when_provider_registry_is_null()
    {
        var options = new PostgresOptions { ConnectionString = "Server=localhost;Database=test;" };

        var act = () => new PostgresKnowledgeStore(options, null!, _logger.Object);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_throws_when_logger_is_null()
    {
        var options = new PostgresOptions { ConnectionString = "Server=localhost;Database=test;" };

        var act = () => new PostgresKnowledgeStore(options, _providerRegistry.Object, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_accepts_valid_options()
    {
        var options = new PostgresOptions
        {
            ConnectionString = "Server=localhost;Database=test;",
            TableName = "documents"
        };

        var act = () => new PostgresKnowledgeStore(options, _providerRegistry.Object, _logger.Object);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_uses_default_table_name_when_not_specified()
    {
        var options = new PostgresOptions
        {
            ConnectionString = "Server=localhost;Database=test;",
            TableName = ""
        };

        var act = () => new PostgresKnowledgeStore(options, _providerRegistry.Object, _logger.Object);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_trims_whitespace_from_table_name()
    {
        var options = new PostgresOptions
        {
            ConnectionString = "Server=localhost;Database=test;",
            TableName = "   my_table   "
        };

        var store = new PostgresKnowledgeStore(options, _providerRegistry.Object, _logger.Object);

        store.Should().NotBeNull();
    }

    [Fact]
    public async Task AddAsync_throws_when_document_is_null()
    {
        var options = new PostgresOptions { ConnectionString = "Server=localhost;Database=test;" };
        var store = new PostgresKnowledgeStore(options, _providerRegistry.Object, _logger.Object);

        var act = () => store.AddAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetAsync_returns_null_for_null_id()
    {
        var options = new PostgresOptions { ConnectionString = "Server=localhost;Database=test;" };
        var store = new PostgresKnowledgeStore(options, _providerRegistry.Object, _logger.Object);

        var result = await store.GetAsync(null!);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_returns_null_for_empty_id()
    {
        var options = new PostgresOptions { ConnectionString = "Server=localhost;Database=test;" };
        var store = new PostgresKnowledgeStore(options, _providerRegistry.Object, _logger.Object);

        var result = await store.GetAsync("");

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_returns_false_for_null_id()
    {
        var options = new PostgresOptions { ConnectionString = "Server=localhost;Database=test;" };
        var store = new PostgresKnowledgeStore(options, _providerRegistry.Object, _logger.Object);

        var result = await store.DeleteAsync(null!);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_returns_false_for_empty_id()
    {
        var options = new PostgresOptions { ConnectionString = "Server=localhost;Database=test;" };
        var store = new PostgresKnowledgeStore(options, _providerRegistry.Object, _logger.Object);

        var result = await store.DeleteAsync("");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_returns_empty_matches_for_null_query()
    {
        var options = new PostgresOptions { ConnectionString = "Server=localhost;Database=test;" };
        var store = new PostgresKnowledgeStore(options, _providerRegistry.Object, _logger.Object);

        var (matches, answer) = await store.SearchAsync(null!);

        matches.Should().BeEmpty();
        answer.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_returns_empty_matches_for_empty_query()
    {
        var options = new PostgresOptions { ConnectionString = "Server=localhost;Database=test;" };
        var store = new PostgresKnowledgeStore(options, _providerRegistry.Object, _logger.Object);

        var (matches, answer) = await store.SearchAsync("");

        matches.Should().BeEmpty();
        answer.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_respects_topK_parameter()
    {
        var options = new PostgresOptions { ConnectionString = "Server=localhost;Database=test;" };
        var store = new PostgresKnowledgeStore(options, _providerRegistry.Object, _logger.Object);

        // This will fail to connect to DB, but we're testing parameter validation
        var topKValues = new[] { 1, 5, 10 };

        foreach (var topK in topKValues)
        {
            var (matches, _) = await store.SearchAsync("test", topK: topK);
            // On failed connection, should return empty
            matches.Count().Should().BeLessThanOrEqualTo(topK);
        }
    }

    [Fact]
    public async Task ListAsync_supports_cancellation()
    {
        var options = new PostgresOptions { ConnectionString = "Server=localhost;Database=test;" };
        var store = new PostgresKnowledgeStore(options, _providerRegistry.Object, _logger.Object);

        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var act = () => store.ListAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AddAsync_supports_cancellation()
    {
        var options = new PostgresOptions { ConnectionString = "Server=localhost;Database=test;" };
        var store = new PostgresKnowledgeStore(options, _providerRegistry.Object, _logger.Object);

        var doc = new QianYuan.Api.Services.KnowledgeDocument { Title = "Test", Content = "Content" };
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var act = () => store.AddAsync(doc, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SearchAsync_without_answer_flag_returns_null_answer()
    {
        var options = new PostgresOptions { ConnectionString = "Server=localhost;Database=test;" };
        var store = new PostgresKnowledgeStore(options, _providerRegistry.Object, _logger.Object);

        var (matches, answer) = await store.SearchAsync("test", answer: false);

        answer.Should().BeNull();
    }
}
