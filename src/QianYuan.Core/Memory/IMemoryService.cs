namespace QianYuan.Core.Memory;

public sealed record MemoryContext(
    string? WorkspacePath,
    string? WorkspaceLabel,
    string? OwnerId,
    string? SessionId);

public sealed record MemorySnapshot(
    string? UserMemory,
    string? WorkspaceMemory,
    string? TodayLog,
    string UserMemoryPath,
    string WorkspaceMemoryPath,
    string DailyLogPath);

public interface IMemoryService
{
    ValueTask<MemorySnapshot> ReadAsync(MemoryContext context, CancellationToken ct = default);
    ValueTask WriteMemoryAsync(MemoryContext context, string scope, string content, CancellationToken ct = default);
    ValueTask AppendDailyLogAsync(MemoryContext context, string title, string? userText, string? assistantText, CancellationToken ct = default);
}
