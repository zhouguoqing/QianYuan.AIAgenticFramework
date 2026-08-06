using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QianYuan.Api.Models;
using QianYuan.Api.Services;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;
using QianYuan.Core.Streaming;

namespace QianYuan.Api.Controllers;

[ApiController]
[Route("api/experts")]
public sealed class ExpertsController : ControllerBase
{
    private readonly IExpertCatalogService _catalog;
    private readonly ICustomExpertService _custom;
    private readonly IAgentExecutionService _agentExecution;
    private readonly ILlmProviderRegistry _providers;

    public ExpertsController(
        IExpertCatalogService catalog,
        ICustomExpertService custom,
        IAgentExecutionService agentExecution,
        ILlmProviderRegistry providers)
    {
        _catalog = catalog;
        _custom = custom;
        _agentExecution = agentExecution;
        _providers = providers;
    }

    [HttpGet("categories")]
    public ActionResult<IReadOnlyList<ExpertCategoryDto>> Categories() =>
        Ok(_catalog.ListCategories());

    [HttpGet("scenarios")]
    public ActionResult<IReadOnlyList<ExpertScenarioDto>> Scenarios() =>
        Ok(_catalog.ListScenarios());

    [HttpGet]
    public async Task<ActionResult<ExpertListResultDto>> List(
        [FromQuery] string? category,
        [FromQuery] string? type,
        [FromQuery] string? q,
        [FromQuery] string? sort,
        [FromQuery] string? tag,
        [FromQuery] string? author,
        [FromQuery] bool? isCustom,
        CancellationToken ct)
    {
        var userId = TryGetUserId();
        var items = new List<ExpertSummaryDto>();

        if (isCustom != true)
        {
            items.AddRange(FilterSummaries(_catalog.ListExperts(category, type, q, sort).Items, tag, author));
        }

        if (isCustom != false)
        {
            items.AddRange(await _custom.ListAsync(userId, category, type, q, sort, tag, author, ct).ConfigureAwait(false));
        }

        if (string.Equals(sort, "newest", StringComparison.OrdinalIgnoreCase))
            items = items.OrderByDescending(i => i.IsCustom).ToList();

        return Ok(new ExpertListResultDto(items.Count, items));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ExpertDetailDto>> Get(string id, CancellationToken ct)
    {
        var custom = await _custom.GetAsync(TryGetUserId(), id, ct).ConfigureAwait(false);
        if (custom is not null) return Ok(custom);

        var expert = _catalog.GetExpert(id);
        return expert is null ? NotFound() : Ok(expert);
    }

    [HttpGet("{id}/prompt")]
    public async Task<ActionResult<ExpertPromptDto>> Prompt(string id, CancellationToken ct)
    {
        var custom = await _custom.GetAsync(TryGetUserId(), id, ct).ConfigureAwait(false);
        if (custom is not null)
        {
            var customPrompt = await _custom.GetPromptAsync(TryGetUserId(), id, ct).ConfigureAwait(false);
            return Ok(new ExpertPromptDto(id, customPrompt ?? string.Empty, custom.BoundAgentId));
        }

        var expert = _catalog.GetExpert(id);
        var prompt = await _catalog.GetPromptAsync(id, ct).ConfigureAwait(false);
        return prompt is null || expert is null
            ? NotFound()
            : Ok(new ExpertPromptDto(id, prompt, expert.BoundAgentId));
    }

    [Authorize]
    [HttpPost("custom")]
    public async Task<ActionResult<ExpertDetailDto>> CreateCustom(CustomExpertUpsertRequest request, CancellationToken ct)
    {
        try
        {
            var expert = await _custom.CreateAsync(GetUserId(), request, ct).ConfigureAwait(false);
            return CreatedAtAction(nameof(Get), new { id = expert.Id }, expert);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpPut("custom/{id}")]
    public async Task<ActionResult<ExpertDetailDto>> UpdateCustom(string id, CustomExpertUpsertRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _custom.UpdateAsync(GetUserId(), id, request, ct).ConfigureAwait(false));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [Authorize]
    [HttpPut("custom/{id}/agent")]
    public async Task<ActionResult<ExpertDetailDto>> BindAgent(string id, ExpertBindAgentRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _custom.BindAgentAsync(GetUserId(), id, request.BoundAgentId, ct).ConfigureAwait(false));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [Authorize]
    [HttpDelete("custom/{id}")]
    public async Task<IActionResult> DeleteCustom(string id, CancellationToken ct)
    {
        try
        {
            await _custom.DeleteAsync(GetUserId(), id, ct).ConfigureAwait(false);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("{id}/chat")]
    public async Task<ActionResult<ExpertChatResponse>> Chat(string id, ExpertChatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message) && string.IsNullOrWhiteSpace(request.QuickPrompt))
            return BadRequest("消息不能为空。");

        var (expert, prompt) = await ResolveExpertAndPromptAsync(id, ct).ConfigureAwait(false);
        if (expert is null || string.IsNullOrWhiteSpace(prompt)) return NotFound();

        var userMessage = string.Join("\n\n", new[] { request.QuickPrompt, request.Message }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(expert.BoundAgentId))
            return Ok(await ChatWithBoundAgentAsync(expert, prompt, userMessage, ct).ConfigureAwait(false));

        return Ok(await ChatWithProviderAsync(expert, prompt, userMessage, request.Provider, request.Model, ct).ConfigureAwait(false));
    }

    private async Task<(ExpertDetailDto? Expert, string? Prompt)> ResolveExpertAndPromptAsync(string id, CancellationToken ct)
    {
        var userId = TryGetUserId();
        var custom = await _custom.GetAsync(userId, id, ct).ConfigureAwait(false);
        if (custom is not null)
            return (custom, await _custom.GetPromptAsync(userId, id, ct).ConfigureAwait(false));

        var expert = _catalog.GetExpert(id);
        return expert is null ? (null, null) : (expert, await _catalog.GetPromptAsync(id, ct).ConfigureAwait(false));
    }

    private async Task<ExpertChatResponse> ChatWithBoundAgentAsync(ExpertDetailDto expert, string prompt, string userMessage, CancellationToken ct)
    {
        var chunks = new List<string>();
        var content = new StringBuilder();
        var enrichedMessage = $"专家设定：\n{prompt}\n\n用户请求：\n{userMessage}";

        await foreach (var raw in _agentExecution.InteractAsync(expert.BoundAgentId!, enrichedMessage, ct).ConfigureAwait(false))
        {
            var text = ExtractAgentContent(raw);
            if (string.IsNullOrEmpty(text)) continue;
            chunks.Add(text);
            content.Append(text);
        }

        return new ExpertChatResponse(expert.Id, expert.BoundAgentId, content.ToString(), chunks);
    }

    private async Task<ExpertChatResponse> ChatWithProviderAsync(
        ExpertDetailDto expert,
        string prompt,
        string userMessage,
        string? providerId,
        string? model,
        CancellationToken ct)
    {
        var provider = string.IsNullOrWhiteSpace(providerId) ? _providers.Default : _providers.Get(providerId.Trim());
        if (provider is null) throw new InvalidOperationException($"Provider '{providerId}' not found.");

        var chunks = new List<string>();
        var content = new StringBuilder();
        var chat = new ChatRequest
        {
            Messages = new[] { ChatMessage.System(prompt), ChatMessage.User(userMessage) },
            Options = new GenerationOptions
            {
                Model = string.IsNullOrWhiteSpace(model) ? provider.DefaultModel : model.Trim(),
                MaxOutputTokens = 2000,
                Stream = true,
            }
        };

        await foreach (var chunk in provider.StreamAsync(chat, ct).ConfigureAwait(false))
        {
            if (chunk.Kind != StreamingChunkKind.TextDelta || string.IsNullOrEmpty(chunk.Text)) continue;
            chunks.Add(chunk.Text);
            content.Append(chunk.Text);
        }

        return new ExpertChatResponse(expert.Id, null, content.ToString(), chunks);
    }

    private static string? ExtractAgentContent(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.TryGetProperty("content", out var content) ? content.GetString() : raw;
        }
        catch
        {
            return raw;
        }
    }

    private static IEnumerable<ExpertSummaryDto> FilterSummaries(IEnumerable<ExpertSummaryDto> items, string? tag, string? author)
    {
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var value = tag.Trim();
            items = items.Where(e => e.Tags.Any(t => t.Equals(value, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(author))
        {
            var value = author.Trim();
            items = items.Where(e => e.Author?.Contains(value, StringComparison.OrdinalIgnoreCase) == true);
        }

        return items;
    }

    private Guid? TryGetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private Guid GetUserId() => TryGetUserId() ?? throw new UnauthorizedAccessException();
}
