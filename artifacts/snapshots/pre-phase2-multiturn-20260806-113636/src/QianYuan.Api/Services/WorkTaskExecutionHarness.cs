using System.Text;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QianYuan.Api.Models;
using QianYuan.Data;

namespace QianYuan.Api.Services;

public interface IWorkTaskExecutionHarness
{
    Task<WorkTaskDetailDto> RunAsync(Guid userId, Guid taskId, ExecuteTaskRequest request, CancellationToken ct = default);
    Task<WorkTaskRuntimeDto> GetRuntimeAsync(Guid userId, Guid taskId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkTaskRuntimeDto>> ListRuntimesAsync(Guid userId, CancellationToken ct = default);
    Task<WorkTaskRuntimeDto> CancelAsync(Guid userId, Guid taskId, string? reason, CancellationToken ct = default);
}

public sealed class WorkTaskExecutionHarness : IWorkTaskExecutionHarness
{
    private sealed class RuntimeEntry
    {
        public required Guid UserId { get; init; }
        public required Guid TaskId { get; init; }
        public required DateTime StartedAt { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public required Task Runner { get; init; }
        public DateTime? FinishedAt { get; set; }
        public string Status { get; set; } = "Running";
        public string? LastError { get; set; }
        public string? CancelReason { get; set; }
    }

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, RuntimeEntry> _runtimes = new(StringComparer.OrdinalIgnoreCase);

    public WorkTaskExecutionHarness(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<WorkTaskDetailDto> RunAsync(Guid userId, Guid taskId, ExecuteTaskRequest request, CancellationToken ct = default)
    {
        var key = RuntimeKey(userId, taskId);
        var existing = _runtimes.GetValueOrDefault(key);
        if (existing is not null && !existing.Runner.IsCompleted)
        {
            return await MustGetTaskAsync(userId, taskId, ct);
        }

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QianYuanDbContext>();
            var task = await db.WorkTasks.FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Work task not found.");
            task.Status = "Running";
            task.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        var linked = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        var startedAt = DateTime.UtcNow;
        RuntimeEntry? holder = null;
        var runner = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var teams = scope.ServiceProvider.GetRequiredService<IExpertTeamService>();
                await teams.ExecuteTaskAsync(userId, taskId, request.TeamId, request.MaxIterations, request.TimeoutSeconds, null, linked.Token);
                if (holder is not null)
                {
                    holder.Status = "Completed";
                    holder.FinishedAt = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException)
            {
                if (holder is not null)
                {
                    holder.Status = "Canceled";
                    holder.FinishedAt = DateTime.UtcNow;
                }
                await MarkTaskStatusAsync(userId, taskId, "Canceled", "任务已取消。", CancellationToken.None);
            }
            catch (Exception ex)
            {
                if (holder is not null)
                {
                    holder.Status = "Failed";
                    holder.LastError = ex.Message;
                    holder.FinishedAt = DateTime.UtcNow;
                }
                await MarkTaskStatusAsync(userId, taskId, "Failed", ex.Message, CancellationToken.None);
            }
        });

        holder = new RuntimeEntry
        {
            UserId = userId,
            TaskId = taskId,
            StartedAt = startedAt,
            Cts = linked,
            Runner = runner,
        };
        _runtimes[key] = holder;
        return await MustGetTaskAsync(userId, taskId, ct);
    }

    public async Task<WorkTaskRuntimeDto> GetRuntimeAsync(Guid userId, Guid taskId, CancellationToken ct = default)
    {
        var key = RuntimeKey(userId, taskId);
        var runtime = _runtimes.GetValueOrDefault(key);
        if (runtime is not null)
        {
            return ToRuntime(runtime);
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QianYuanDbContext>();
        var task = await db.WorkTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Work task not found.");
        return new WorkTaskRuntimeDto(task.Id, task.Status, false, task.UpdatedAt, null, null, null);
    }

    public Task<IReadOnlyList<WorkTaskRuntimeDto>> ListRuntimesAsync(Guid userId, CancellationToken ct = default)
    {
        var list = _runtimes.Values
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.StartedAt)
            .Select(ToRuntime)
            .ToList();
        return Task.FromResult<IReadOnlyList<WorkTaskRuntimeDto>>(list);
    }

    public async Task<WorkTaskRuntimeDto> CancelAsync(Guid userId, Guid taskId, string? reason, CancellationToken ct = default)
    {
        var key = RuntimeKey(userId, taskId);
        var runtime = _runtimes.GetValueOrDefault(key);
        if (runtime is not null && !runtime.Runner.IsCompleted)
        {
            runtime.CancelReason = string.IsNullOrWhiteSpace(reason) ? "manual" : reason.Trim();
            runtime.Status = "CancelRequested";
            runtime.Cts.Cancel();
            return ToRuntime(runtime);
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QianYuanDbContext>();
        var task = await db.WorkTasks.FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Work task not found.");
        task.Status = "Canceled";
        task.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new WorkTaskRuntimeDto(task.Id, task.Status, false, task.UpdatedAt, null, null, reason);
    }

    private static string RuntimeKey(Guid userId, Guid taskId) => $"{userId:N}:{taskId:N}";

    private static WorkTaskRuntimeDto ToRuntime(RuntimeEntry runtime)
    {
        return new WorkTaskRuntimeDto(
            runtime.TaskId,
            runtime.Status,
            !runtime.Runner.IsCompleted,
            runtime.StartedAt,
            runtime.FinishedAt,
            runtime.LastError,
            runtime.CancelReason);
    }

    private async Task<WorkTaskDetailDto> MustGetTaskAsync(Guid userId, Guid taskId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<IWorkTaskService>();
        var task = await tasks.GetAsync(userId, taskId, ct);
        return task ?? throw new KeyNotFoundException("Work task not found.");
    }

    private async Task MarkTaskStatusAsync(Guid userId, Guid taskId, string status, string message, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QianYuanDbContext>();
        var task = await db.WorkTasks.FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct);
        if (task is null) return;

        task.Status = status;
        task.UpdatedAt = DateTime.UtcNow;
        db.WorkArtifacts.Add(new Data.Entities.WorkArtifact
        {
            TaskId = task.Id,
            UserId = userId,
            Name = $"任务执行状态-{status}.md",
            ContentType = "text/markdown",
            StorageKind = "Database",
            Content = $"# 任务状态更新\n\n- Status: {status}\n- Message: {message}\n- Time(UTC): {DateTime.UtcNow:O}\n",
            SizeBytes = Encoding.UTF8.GetByteCount($"{status}\n{message}"),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}