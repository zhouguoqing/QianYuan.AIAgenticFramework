using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QianYuan.Api.Models;
using QianYuan.Api.Services;

namespace QianYuan.Api.Controllers;

[ApiController]
[Route("api/work-tasks")]
[Authorize]
public sealed class WorkTasksController : ControllerBase
{
    private readonly IWorkTaskService _tasks;
    private readonly IWorkTaskExecutionHarness _harness;

    public WorkTasksController(IWorkTaskService tasks, IWorkTaskExecutionHarness harness)
    {
        _tasks = tasks;
        _harness = harness;
    }

    [HttpPost]
    public async Task<ActionResult<WorkTaskDetailDto>> Create(CreateWorkTaskRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _tasks.CreateAsync(GetUserId(), request, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkTaskDto>>> List([FromQuery] int take = 50, CancellationToken ct = default)
    {
        return Ok(await _tasks.ListAsync(GetUserId(), take, ct));
    }

    [HttpGet("{taskId:guid}")]
    public async Task<ActionResult<WorkTaskDetailDto>> Get(Guid taskId, CancellationToken ct)
    {
        var task = await _tasks.GetAsync(GetUserId(), taskId, ct);
        return task is null ? NotFound() : Ok(task);
    }

    [HttpGet("{taskId:guid}/artifacts")]
    public async Task<ActionResult<IReadOnlyList<WorkArtifactDto>>> Artifacts(Guid taskId, CancellationToken ct)
    {
        return Ok(await _tasks.ListArtifactsAsync(GetUserId(), taskId, ct));
    }

    [HttpGet("/api/artifacts/{artifactId:guid}")]
    public async Task<ActionResult<WorkArtifactDto>> Artifact(Guid artifactId, CancellationToken ct)
    {
        var artifact = await _tasks.GetArtifactAsync(GetUserId(), artifactId, ct);
        return artifact is null ? NotFound() : Ok(artifact);
    }

    [HttpPost("{taskId:guid}/run")]
    public async Task<ActionResult<WorkTaskDetailDto>> Run(Guid taskId, ExecuteTaskRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _harness.RunAsync(GetUserId(), taskId, request, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("{taskId:guid}/runtime")]
    public async Task<ActionResult<WorkTaskRuntimeDto>> Runtime(Guid taskId, CancellationToken ct)
    {
        try
        {
            return Ok(await _harness.GetRuntimeAsync(GetUserId(), taskId, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("runtimes")]
    public async Task<ActionResult<IReadOnlyList<WorkTaskRuntimeDto>>> Runtimes(CancellationToken ct)
    {
        return Ok(await _harness.ListRuntimesAsync(GetUserId(), ct));
    }

    [HttpPost("{taskId:guid}/cancel")]
    public async Task<ActionResult<WorkTaskRuntimeDto>> Cancel(Guid taskId, CancelWorkTaskRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _harness.CancelAsync(GetUserId(), taskId, request.Reason, ct));
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