using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QianYuan.Api.Services;

namespace QianYuan.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class KnowledgeController : ControllerBase
{
    private readonly IKnowledgeStore _store;
    private readonly IKnowledgeDocumentParser _parser;

    public KnowledgeController(IKnowledgeStore store, IKnowledgeDocumentParser parser)
    {
        _store = store;
        _parser = parser;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await _store.ListAsync(ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var d = await _store.GetAsync(id, ct);
        return d is null ? NotFound() : Ok(d);
    }

    public sealed class UploadRequest { public string Title { get; set; } = ""; public string Content { get; set; } = ""; public string[]? Tags { get; set; } }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload([FromBody] UploadRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Content)) return BadRequest(new { error = "content required" });
        var doc = new KnowledgeDocument { Title = req.Title ?? "(untitled)", Content = req.Content, Tags = req.Tags ?? Array.Empty<string>() };
        await _store.AddAsync(doc, ct);
        return Ok(doc);
    }

    [HttpPost("upload-file")]
    public async Task<IActionResult> UploadFile([FromForm] IFormFile file, [FromForm] string? title, [FromForm] string? tags, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "file required" });

        var tagList = string.IsNullOrWhiteSpace(tags)
            ? Array.Empty<string>()
            : tags.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();

        await using var stream = file.OpenReadStream();
        var docs = await _parser.ParseAsync(stream, file.FileName, string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(file.FileName) : title, tagList, ct).ConfigureAwait(false);
        foreach (var doc in docs)
        {
            await _store.AddAsync(doc, ct).ConfigureAwait(false);
        }

        return Ok(new { documents = docs });
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int topK = 5, [FromQuery] bool answer = false, [FromQuery] string? provider = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q)) return BadRequest(new { error = "q required" });
        var (matches, ans) = await _store.SearchAsync(q, topK, answer, provider, ct);
        return Ok(new { matches, answer = ans });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var ok = await _store.DeleteAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }
}
