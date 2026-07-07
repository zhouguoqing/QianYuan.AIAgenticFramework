using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QianYuan.Api.Models;
using QianYuan.Api.Services;

namespace QianYuan.Api.Controllers;

[ApiController]
[Route("api/expert-teams")]
[Authorize]
public sealed class ExpertTeamsController : ControllerBase
{
    private readonly IExpertTeamService _teams;

    public ExpertTeamsController(IExpertTeamService teams)
    {
        _teams = teams;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExpertTeamDto>>> List(CancellationToken ct)
    {
        return Ok(await _teams.ListAsync(GetUserId(), ct));
    }

    [HttpGet("{teamId:guid}")]
    public async Task<ActionResult<ExpertTeamDto>> Get(Guid teamId, CancellationToken ct)
    {
        var team = await _teams.GetAsync(GetUserId(), teamId, ct);
        return team is null ? NotFound() : Ok(team);
    }

    [HttpPost]
    public async Task<ActionResult<ExpertTeamDto>> Create(CreateExpertTeamRequest request, CancellationToken ct)
    {
        try
        {
            var team = await _teams.CreateAsync(GetUserId(), request, ct);
            return CreatedAtAction(nameof(Get), new { teamId = team.Id }, team);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("/api/work-tasks/{taskId:guid}/orchestrate")]
    public async Task<ActionResult<WorkTaskDetailDto>> Orchestrate(Guid taskId, OrchestrateTaskRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _teams.OrchestrateTaskAsync(GetUserId(), taskId, request.TeamId, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("/api/work-tasks/{taskId:guid}/execute")]
    public async Task<ActionResult<WorkTaskDetailDto>> Execute(Guid taskId, ExecuteTaskRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _teams.ExecuteTaskAsync(GetUserId(), taskId, request.TeamId, request.MaxIterations, request.TimeoutSeconds, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private Guid GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : throw new UnauthorizedAccessException();
    }
}