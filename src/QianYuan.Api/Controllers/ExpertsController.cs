using Microsoft.AspNetCore.Mvc;
using QianYuan.Api.Models;
using QianYuan.Api.Services;

namespace QianYuan.Api.Controllers;

[ApiController]
[Route("api/experts")]
public sealed class ExpertsController : ControllerBase
{
    private readonly IExpertCatalogService _catalog;

    public ExpertsController(IExpertCatalogService catalog)
    {
        _catalog = catalog;
    }

    [HttpGet("categories")]
    public ActionResult<IReadOnlyList<ExpertCategoryDto>> Categories() =>
        Ok(_catalog.ListCategories());

    [HttpGet("scenarios")]
    public ActionResult<IReadOnlyList<ExpertScenarioDto>> Scenarios() =>
        Ok(_catalog.ListScenarios());

    [HttpGet]
    public ActionResult<ExpertListResultDto> List(
        [FromQuery] string? category,
        [FromQuery] string? type,
        [FromQuery] string? q,
        [FromQuery] string? sort) =>
        Ok(_catalog.ListExperts(category, type, q, sort));

    [HttpGet("{id}")]
    public ActionResult<ExpertDetailDto> Get(string id)
    {
        var expert = _catalog.GetExpert(id);
        return expert is null ? NotFound() : Ok(expert);
    }

    [HttpGet("{id}/prompt")]
    public async Task<ActionResult<object>> Prompt(string id, CancellationToken ct)
    {
        var prompt = await _catalog.GetPromptAsync(id, ct);
        return prompt is null ? NotFound() : Ok(new { id, systemPrompt = prompt });
    }
}
