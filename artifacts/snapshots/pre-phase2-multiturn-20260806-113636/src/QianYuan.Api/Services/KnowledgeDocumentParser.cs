using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using HtmlAgilityPack;
using Markdig;
using Tesseract;
using UglyToad.PdfPig;

namespace QianYuan.Api.Services;

public interface IKnowledgeDocumentParser
{
    Task<IReadOnlyList<KnowledgeDocument>> ParseAsync(Stream stream, string fileName, string title, string[]? tags, CancellationToken ct = default);
}

public sealed class KnowledgeDocumentParser : IKnowledgeDocumentParser
{
    private static readonly string[] TextExtensions = { ".txt", ".md", ".markdown", ".html", ".htm" };
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };
    private static readonly string[] OfficeExtensions = { ".docx", ".xlsx", ".pptx" };

    public async Task<IReadOnlyList<KnowledgeDocument>> ParseAsync(Stream stream, string fileName, string title, string[]? tags, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var content = ext switch
        {
            ".pdf" => await ParsePdfAsync(stream, ct).ConfigureAwait(false),
            ".docx" => ParseDocx(stream),
            ".xlsx" => ParseXlsx(stream),
            ".pptx" => ParsePptx(stream),
            _ when TextExtensions.Contains(ext) => await ParseTextAsync(stream, ext, ct).ConfigureAwait(false),
            _ when ImageExtensions.Contains(ext) => ParseImage(stream),
            _ => await ParseTextAsync(stream, ext, ct).ConfigureAwait(false),
        };

        var chunks = SplitText(content);
        var docs = new List<KnowledgeDocument>();
        var prefix = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(fileName) : title.Trim();
        var sectionCount = Math.Max(chunks.Count, 1);

        for (var i = 0; i < chunks.Count; i++)
        {
            docs.Add(new KnowledgeDocument
            {
                Title = sectionCount == 1 ? prefix : $"{prefix} — part {i + 1}/{sectionCount}",
                Content = chunks[i],
                Tags = tags ?? Array.Empty<string>(),
                SourceFile = Path.GetFileName(fileName),
                SourceSection = sectionCount == 1 ? "full" : $"part {i + 1}/{sectionCount}",
            });
        }

        if (docs.Count == 0)
        {
            docs.Add(new KnowledgeDocument
            {
                Title = prefix,
                Content = string.Empty,
                Tags = tags ?? Array.Empty<string>(),
                SourceFile = Path.GetFileName(fileName),
                SourceSection = "full",
            });
        }

        return docs;
    }

    private static async Task<string> ParsePdfAsync(Stream stream, CancellationToken ct)
    {
        using var document = PdfDocument.Open(stream);
        var builder = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            if (ct.IsCancellationRequested) break;
            builder.AppendLine(page.Text);
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string ParseText(Stream stream, string extension)
    {
        stream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
        var text = reader.ReadToEnd();
        if (extension is ".md" or ".markdown")
        {
            var html = Markdown.ToHtml(text);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc.DocumentNode.InnerText;
        }

        if (extension is ".html" or ".htm")
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(text);
            return doc.DocumentNode.InnerText;
        }

        return text;
    }

    private static async Task<string> ParseTextAsync(Stream stream, string extension, CancellationToken ct)
    {
        stream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
        var text = await reader.ReadToEndAsync().ConfigureAwait(false);
        if (extension is ".md" or ".markdown")
        {
            var html = Markdown.ToHtml(text);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc.DocumentNode.InnerText;
        }

        if (extension is ".html" or ".htm")
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(text);
            return doc.DocumentNode.InnerText;
        }

        return text;
    }

    private static string ParseDocx(Stream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        using var package = WordprocessingDocument.Open(stream, false);
        return package.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
    }

    private static string ParseXlsx(Stream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        using var package = SpreadsheetDocument.Open(stream, false);
        var builder = new StringBuilder();
        foreach (var sheet in package.WorkbookPart?.Workbook?.Descendants<DocumentFormat.OpenXml.Spreadsheet.Sheet>() ?? Enumerable.Empty<DocumentFormat.OpenXml.Spreadsheet.Sheet>())
        {
            if (string.IsNullOrEmpty(sheet.Id)) continue;
            var worksheetPart = (WorksheetPart?)package.WorkbookPart?.GetPartById(sheet.Id);
            if (worksheetPart?.Worksheet is null) continue;
            builder.AppendLine(sheet.Name ?? string.Empty);
            var cells = worksheetPart.Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>();
            foreach (var cell in cells)
            {
                builder.Append(cell.InnerText);
                builder.Append(' ');
            }
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string ParsePptx(Stream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        using var package = PresentationDocument.Open(stream, false);
        var builder = new StringBuilder();
        foreach (var slidePart in package.PresentationPart?.SlideParts ?? Enumerable.Empty<SlidePart>())
        {
            if (slidePart.Slide is null) continue;
            builder.AppendLine("Slide");
            foreach (var text in slidePart.Slide.Descendants<DocumentFormat.OpenXml.Drawing.Text>())
            {
                builder.Append(text.Text);
                builder.Append(' ');
            }
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string ParseImage(Stream stream)
    {
        try
        {
            var tessDataPath = GetTessDataPath();
            if (string.IsNullOrWhiteSpace(tessDataPath)) return string.Empty;

            stream.Seek(0, SeekOrigin.Begin);
            using var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
            using var image = Pix.LoadFromMemory(ReadAllBytes(stream));
            using var page = engine.Process(image);
            return page.GetText() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? GetTessDataPath()
    {
        var candidate = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            if (Directory.Exists(candidate)) return candidate;
            if (Directory.Exists(Path.Combine(candidate, "tessdata"))) return Path.Combine(candidate, "tessdata");
        }

        var fallback = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tessdata"),
            "/usr/local/share/tessdata",
            "/usr/share/tessdata",
        };

        return fallback.FirstOrDefault(Directory.Exists);
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static List<string> SplitText(string text)
    {
        var normalized = Regex.Replace(text ?? string.Empty, "\r\n|\r", "\n").Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return new List<string>();

        var paragraphs = normalized.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        var chunks = new List<string>();
        var current = new StringBuilder();
        const int maxChunk = 1600;

        foreach (var paragraph in paragraphs)
        {
            if (current.Length + paragraph.Length + 2 <= maxChunk)
            {
                if (current.Length > 0) current.Append("\n\n");
                current.Append(paragraph);
                continue;
            }

            if (current.Length > 0)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }

            if (paragraph.Length <= maxChunk)
            {
                current.Append(paragraph);
                continue;
            }

            foreach (var segment in SplitLongParagraph(paragraph, maxChunk))
            {
                chunks.Add(segment);
            }
        }

        if (current.Length > 0) chunks.Add(current.ToString());
        return chunks;
    }

    private static IEnumerable<string> SplitLongParagraph(string paragraph, int maxChunk)
    {
        var sentences = Regex.Split(paragraph, "(?<=[.!?])\\s+");
        var current = new StringBuilder();
        foreach (var sentence in sentences)
        {
            if (current.Length + sentence.Length + 1 <= maxChunk)
            {
                if (current.Length > 0) current.Append(' ');
                current.Append(sentence);
                continue;
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }

            if (sentence.Length <= maxChunk)
            {
                current.Append(sentence);
                continue;
            }

            for (var i = 0; i < sentence.Length; i += maxChunk)
            {
                yield return sentence.Substring(i, Math.Min(maxChunk, sentence.Length - i));
            }
        }

        if (current.Length > 0) yield return current.ToString();
    }
}
