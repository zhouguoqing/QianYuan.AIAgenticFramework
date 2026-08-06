using FluentAssertions;
using QianYuan.Api.Services;

namespace QianYuan.Core.Tests;

public class KnowledgeDocumentParserTests
{
    private readonly KnowledgeDocumentParser _parser = new();

    [Fact]
    public async Task ParseAsync_plain_text_file()
    {
        var content = "This is a simple test document.\n\nIt has multiple paragraphs.\n\nAnd more content here.";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _parser.ParseAsync(stream, "test.txt", "Test Document", new[] { "test" });

        result.Should().NotBeEmpty();
        result.All(d => d.Title.Contains("Test Document")).Should().BeTrue();
        result.All(d => d.Tags.Contains("test")).Should().BeTrue();
    }

    [Fact]
    public async Task ParseAsync_markdown_file()
    {
        var content = "# Heading\n\nThis is **markdown** content.\n\n## Section\n\nMore content here.";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _parser.ParseAsync(stream, "test.md", "Markdown Doc", null);

        result.Should().NotBeEmpty();
        result.All(d => d.SourceFile == "test.md").Should().BeTrue();
    }

    [Fact]
    public async Task ParseAsync_handles_empty_file()
    {
        var content = "";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _parser.ParseAsync(stream, "empty.txt", "Empty", null);

        result.Should().HaveCount(1);
        result[0].Content.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsync_splits_large_content_into_chunks()
    {
        var content = string.Join("\n\n", Enumerable.Range(1, 100).Select(i => $"Paragraph {i}: " + string.Join(" ", Enumerable.Range(1, 30).Select(_ => "word"))));
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _parser.ParseAsync(stream, "large.txt", "Large Doc", null);

        result.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task ParseAsync_uses_filename_as_title_when_empty()
    {
        var content = "Test content";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _parser.ParseAsync(stream, "myfile.txt", "", null);

        result[0].Title.Should().Contain("myfile");
    }

    [Fact]
    public async Task ParseAsync_includes_source_file_information()
    {
        var content = "Test content";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _parser.ParseAsync(stream, "source.txt", "Doc", null);

        result.All(d => d.SourceFile == "source.txt").Should().BeTrue();
    }

    [Fact]
    public async Task ParseAsync_handles_multiple_tags()
    {
        var content = "Content here";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        var tags = new[] { "tag1", "tag2", "tag3" };

        var result = await _parser.ParseAsync(stream, "test.txt", "Doc", tags);

        result.All(d => d.Tags.SequenceEqual(tags)).Should().BeTrue();
    }

    [Fact]
    public async Task ParseAsync_handles_whitespace_in_title()
    {
        var content = "Content";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _parser.ParseAsync(stream, "test.txt", "   Title with Spaces   ", null);

        result[0].Title.Should().Be("Title with Spaces");
    }

    [Fact]
    public async Task ParseAsync_single_paragraph_not_split()
    {
        var content = "This is a short single paragraph that should not be split.";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _parser.ParseAsync(stream, "test.txt", "Title", null);

        result.Should().HaveCount(1);
        result[0].SourceSection.Should().Be("full");
    }

    [Fact]
    public async Task ParseAsync_multiple_chunks_have_correct_source_section()
    {
        var paragraphs = Enumerable.Range(1, 50).Select(i => $"Paragraph {i}: " + string.Join(" ", Enumerable.Range(1, 20).Select(_ => "word")));
        var content = string.Join("\n\n", paragraphs);
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _parser.ParseAsync(stream, "test.txt", "Title", null);

        if (result.Count > 1)
        {
            for (int i = 0; i < result.Count; i++)
            {
                result[i].SourceSection.Should().Contain((i + 1).ToString());
            }
        }
    }

    [Fact]
    public async Task ParseAsync_html_strips_tags()
    {
        var content = "<html><body><p>Hello <strong>World</strong></p></body></html>";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _parser.ParseAsync(stream, "test.html", "HTML Doc", null);

        result.Should().NotBeEmpty();
        // HTML tags should be stripped
        result[0].Content.Should().NotContain("<");
    }
}
