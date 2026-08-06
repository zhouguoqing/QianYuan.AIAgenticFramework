using FluentAssertions;
using QianYuan.Api.Services;

namespace QianYuan.Core.Tests;

public class KnowledgeSearchTests
{
    [Fact]
    public void ScoreByKeyword_returns_zero_for_no_matches()
    {
        var doc = new KnowledgeDocument
        {
            Title = "Document Title",
            Content = "Some content here",
            Tags = new[] { "tag1", "tag2" }
        };

        var score = KnowledgeSearch.ScoreByKeyword("xyz", doc);

        score.Should().Be(0);
    }

    [Fact]
    public void ScoreByKeyword_finds_matches_in_title()
    {
        var doc = new KnowledgeDocument
        {
            Title = "Machine Learning Guide",
            Content = "This is about programming",
            Tags = Array.Empty<string>()
        };

        var score = KnowledgeSearch.ScoreByKeyword("machine", doc);

        score.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ScoreByKeyword_finds_matches_in_content()
    {
        var doc = new KnowledgeDocument
        {
            Title = "Programming",
            Content = "Machine learning is a subset of AI",
            Tags = Array.Empty<string>()
        };

        var score = KnowledgeSearch.ScoreByKeyword("learning", doc);

        score.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ScoreByKeyword_finds_matches_in_tags()
    {
        var doc = new KnowledgeDocument
        {
            Title = "Article",
            Content = "Some content",
            Tags = new[] { "machine-learning", "ai", "python" }
        };

        var score = KnowledgeSearch.ScoreByKeyword("machine", doc);

        score.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ScoreByKeyword_is_case_insensitive()
    {
        var doc = new KnowledgeDocument
        {
            Title = "MACHINE Learning",
            Content = "Deep Learning",
            Tags = new[] { "AI" }
        };

        var score1 = KnowledgeSearch.ScoreByKeyword("machine", doc);
        var score2 = KnowledgeSearch.ScoreByKeyword("MACHINE", doc);

        score1.Should().Be(score2);
    }

    [Fact]
    public void ScoreByKeyword_counts_multiple_occurrences()
    {
        var doc = new KnowledgeDocument
        {
            Title = "Learning Learning",
            Content = "Machine learning is learning",
            Tags = Array.Empty<string>()
        };

        var score = KnowledgeSearch.ScoreByKeyword("learning", doc);

        score.Should().BeGreaterThan(1);
    }

    [Fact]
    public void ScoreByKeyword_higher_score_for_more_matches()
    {
        var doc1 = new KnowledgeDocument
        {
            Title = "Python",
            Content = "Python programming language",
            Tags = Array.Empty<string>()
        };

        var doc2 = new KnowledgeDocument
        {
            Title = "Python Python",
            Content = "Python Python Python",
            Tags = new[] { "python" }
        };

        var score1 = KnowledgeSearch.ScoreByKeyword("python", doc1);
        var score2 = KnowledgeSearch.ScoreByKeyword("python", doc2);

        score2.Should().BeGreaterThan(score1);
    }

    [Fact]
    public void Tokenize_splits_by_non_word_characters()
    {
        var tokens = KnowledgeSearch.Tokenize("hello world machine-learning ai");

        tokens.Should().Contain("hello");
        tokens.Should().Contain("world");
        tokens.Should().Contain("machine");
        tokens.Should().Contain("learning");
        tokens.Should().Contain("ai");
    }

    [Fact]
    public void Tokenize_filters_single_character_tokens()
    {
        var tokens = KnowledgeSearch.Tokenize("a big c dog");

        tokens.Should().NotContain("a");
        tokens.Should().NotContain("c");
        tokens.Should().Contain("big");
        tokens.Should().Contain("dog");
    }

    [Fact]
    public void Tokenize_converts_to_lowercase()
    {
        var tokens = KnowledgeSearch.Tokenize("Hello World MACHINE Learning");

        tokens.Should().Contain("hello");
        tokens.Should().Contain("world");
        tokens.Should().Contain("machine");
        tokens.Should().Contain("learning");
    }

    [Fact]
    public void Tokenize_handles_punctuation()
    {
        var tokens = KnowledgeSearch.Tokenize("hello, world! machine-learning; AI.");

        tokens.Should().NotBeEmpty();
        tokens.Should().OnlyContain(t => t.Length > 1);
    }

    [Fact]
    public void Tokenize_returns_empty_for_empty_input()
    {
        var tokens = KnowledgeSearch.Tokenize("");

        tokens.Should().BeEmpty();
    }

    [Fact]
    public void ScoreByKeyword_handles_document_without_tags()
    {
        var doc = new KnowledgeDocument
        {
            Title = "Title",
            Content = "Content here",
            Tags = null
        };

        var act = () => KnowledgeSearch.ScoreByKeyword("here", doc);

        act.Should().NotThrow();
    }

    [Fact]
    public void ScoreByKeyword_multiple_word_query()
    {
        var doc = new KnowledgeDocument
        {
            Title = "Artificial Intelligence",
            Content = "Machine Learning and Deep Neural Networks",
            Tags = new[] { "ai", "ml" }
        };

        var score1 = KnowledgeSearch.ScoreByKeyword("artificial intelligence", doc);
        var score2 = KnowledgeSearch.ScoreByKeyword("neural networks", doc);

        score1.Should().BeGreaterThan(0);
        score2.Should().BeGreaterThan(0);
    }
}
