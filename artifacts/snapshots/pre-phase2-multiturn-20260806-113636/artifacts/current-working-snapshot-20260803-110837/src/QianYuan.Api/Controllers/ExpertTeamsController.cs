using System.Security.Claims;
using System.Text.Json;
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
    private readonly IExpertTeamTemplateService _templates;

    public ExpertTeamsController(IExpertTeamService teams, IExpertTeamTemplateService templates)
    {
        _teams = teams;
        _templates = templates;
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

    [HttpGet("templates")]
    public ActionResult<IReadOnlyList<ExpertTeamTemplateDto>> Templates()
    {
        return Ok(_templates.ListTemplates());
    }

    [HttpPost("from-template/{templateId}")]
    public async Task<ActionResult<ExpertTeamDto>> CreateFromTemplate(string templateId, CancellationToken ct)
    {
        try
        {
            var team = await _teams.CreateFromTemplateAsync(GetUserId(), templateId, ct);
            return CreatedAtAction(nameof(Get), new { teamId = team.Id }, team);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPut("{teamId:guid}")]
    public async Task<ActionResult<ExpertTeamDto>> Update(Guid teamId, UpdateExpertTeamRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _teams.UpdateAsync(GetUserId(), teamId, request, ct));
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

    [HttpDelete("{teamId:guid}")]
    public async Task<IActionResult> Delete(Guid teamId, CancellationToken ct)
    {
        try
        {
            await _teams.DeleteAsync(GetUserId(), teamId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("{teamId:guid}/members")]
    public async Task<ActionResult<ExpertTeamMemberDto>> AddMember(Guid teamId, CreateExpertTeamMemberRequest request, CancellationToken ct)
    {
        try
        {
            var member = await _teams.AddMemberAsync(GetUserId(), teamId, request, ct);
            return CreatedAtAction(nameof(Get), new { teamId }, member);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPut("{teamId:guid}/members/{memberId:guid}")]
    public async Task<ActionResult<ExpertTeamMemberDto>> UpdateMember(Guid teamId, Guid memberId, UpdateExpertTeamMemberRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _teams.UpdateMemberAsync(GetUserId(), teamId, memberId, request, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{teamId:guid}/members/{memberId:guid}")]
    public async Task<IActionResult> DeleteMember(Guid teamId, Guid memberId, CancellationToken ct)
    {
        try
        {
            await _teams.DeleteMemberAsync(GetUserId(), teamId, memberId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
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
            return Ok(await _teams.ExecuteTaskAsync(GetUserId(), taskId, request.TeamId, request.MaxIterations, request.TimeoutSeconds, null, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("/api/work-tasks/{taskId:guid}/execute-stream")]
    public async Task ExecuteStream(Guid taskId, [FromQuery] Guid? teamId, [FromQuery] int? maxIterations, [FromQuery] int? timeoutSeconds, CancellationToken ct)
    {
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache, no-transform";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await _teams.ExecuteTaskAsync(
                GetUserId(),
                taskId,
                teamId,
                maxIterations,
                timeoutSeconds,
                async (evt, token) => await WriteSse(evt.Type, evt, token),
                ct);
        }
        catch (KeyNotFoundException)
        {
            await WriteSse("error", new { message = "not found" }, ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await WriteSse("error", new { message = ex.Message }, ct);
        }
    }

    private async Task WriteSse(string eventName, object data, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var bytes = System.Text.Encoding.UTF8.GetBytes($"event: {eventName}\ndata: {payload}\n\n");
        await Response.Body.WriteAsync(bytes, ct).ConfigureAwait(false);
        await Response.Body.FlushAsync(ct).ConfigureAwait(false);
    }

    private Guid GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : throw new UnauthorizedAccessException();
    }
}