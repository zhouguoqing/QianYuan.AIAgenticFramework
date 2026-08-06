using System.Text;
using Microsoft.EntityFrameworkCore;
using QianYuan.Api.Models;
using QianYuan.Data;
using QianYuan.Data.Entities;

namespace QianYuan.Api.Services;

public interface IWorkTaskService
{
    Task<WorkTaskDetailDto> CreateAsync(Guid userId, CreateWorkTaskRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<WorkTaskDto>> ListAsync(Guid userId, int take, CancellationToken ct = default);
    Task<WorkTaskDetailDto?> GetAsync(Guid userId, Guid taskId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkArtifactDto>> ListArtifactsAsync(Guid userId, Guid taskId, CancellationToken ct = default);
    Task<WorkArtifactDto?> GetArtifactAsync(Guid userId, Guid artifactId, CancellationToken ct = default);
}

public sealed class WorkTaskService : IWorkTaskService
{
    private readonly QianYuanDbContext _db;

    public WorkTaskService(QianYuanDbContext db)
    {
        _db = db;
    }

    public async Task<WorkTaskDetailDto> CreateAsync(Guid userId, CreateWorkTaskRequest request, CancellationToken ct = default)
    {
        var title = string.IsNullOrWhiteSpace(request.Title) ? BuildTitle(request.Goal) : request.Title.Trim();
        if (string.IsNullOrWhiteSpace(request.Goal)) throw new ArgumentException("Goal is required.");

        var now = DateTime.UtcNow;
        var task = new WorkTask
        {
            UserId = userId,
            Title = title,
            Goal = request.Goal.Trim(),
            Status = "Draft",
            TeamId = EmptyToNull(request.TeamId),
            ProviderId = EmptyToNull(request.ProviderId),
            Model = EmptyToNull(request.Model),
            CreatedAt = now,
            UpdatedAt = now,
        };
        var step = new WorkStep
        {
            Task = task,
            UserId = userId,
            StepOrder = 1,
            Name = "任务接收",
            Status = "Completed",
            Summary = "任务已创建，等待专家团编排执行。",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var content = $"# {title}\n\n## 目标\n\n{request.Goal.Trim()}\n\n## 状态\n\n任务已创建，等待后续专家团执行。\n";
        var artifact = new WorkArtifact
        {
            Task = task,
            UserId = userId,
            Name = "任务说明.md",
            ContentType = "text/markdown",
            StorageKind = "Database",
            Content = content,
            SizeBytes = Encoding.UTF8.GetByteCount(content),
            CreatedAt = now,
        };

        _db.WorkTasks.Add(task);
        _db.WorkSteps.Add(step);
        _db.WorkArtifacts.Add(artifact);
        await _db.SaveChangesAsync(ct);

        return (await GetAsync(userId, task.Id, ct))!;
    }

    public async Task<IReadOnlyList<WorkTaskDto>> ListAsync(Guid userId, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);
        return await _db.WorkTasks
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.UpdatedAt)
            .Take(take)
            .Select(t => new WorkTaskDto(t.Id, t.Title, t.Goal, t.Status, t.TeamId, t.ProviderId, t.Model, t.CreatedAt, t.UpdatedAt, t.Steps.Count, t.Artifacts.Count))
            .ToListAsync(ct);
    }

    public async Task<WorkTaskDetailDto?> GetAsync(Guid userId, Guid taskId, CancellationToken ct = default)
    {
        var task = await _db.WorkTasks
            .AsNoTracking()
            .Include(t => t.Steps.OrderBy(s => s.StepOrder))
            .Include(t => t.Artifacts.OrderByDescending(a => a.CreatedAt))
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct);
        if (task is null) return null;

        return new WorkTaskDetailDto(ToDto(task), task.Steps.Select(ToDto).ToList(), task.Artifacts.Select(ToDto).ToList());
    }

    public async Task<IReadOnlyList<WorkArtifactDto>> ListArtifactsAsync(Guid userId, Guid taskId, CancellationToken ct = default)
    {
        return await _db.WorkArtifacts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.TaskId == taskId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => ToDto(a))
            .ToListAsync(ct);
    }

    public async Task<WorkArtifactDto?> GetArtifactAsync(Guid userId, Guid artifactId, CancellationToken ct = default)
    {
        var artifact = await _db.WorkArtifacts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == artifactId && a.UserId == userId, ct);
        return artifact is null ? null : ToDto(artifact);
    }

    private static WorkTaskDto ToDto(WorkTask task)
    {
        return new WorkTaskDto(task.Id, task.Title, task.Goal, task.Status, task.TeamId, task.ProviderId, task.Model, task.CreatedAt, task.UpdatedAt, task.Steps.Count, task.Artifacts.Count);
    }

    private static WorkStepDto ToDto(WorkStep step)
    {
        return new WorkStepDto(step.Id, step.StepOrder, step.Name, step.Status, step.AgentId, step.Summary, step.ExecutionMode, step.CreatedAt, step.UpdatedAt);
    }

    private static WorkArtifactDto ToDto(WorkArtifact artifact)
    {
        return new WorkArtifactDto(artifact.Id, artifact.TaskId, artifact.Name, artifact.ContentType, artifact.StorageKind, artifact.Content, artifact.FilePath, artifact.SizeBytes, artifact.CreatedAt);
    }

    private static string BuildTitle(string goal)
    {
        var normalized = (goal ?? string.Empty).Trim();
        return normalized.Length <= 40 ? normalized : normalized[..40];
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}