using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using QianYuan.Core.Memory;

namespace QianYuan.Api.Services;

public sealed class LocalMemoryService : IMemoryService
{
    private const int MaxReadChars = 16000;
    private const int MaxUserWriteChars = 4000;
    private const int MaxDailyTextChars = 2000;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly IHostEnvironment _environment;
    private readonly ILogger<LocalMemoryService> _logger;

    public LocalMemoryService(IHostEnvironment environment, ILogger<LocalMemoryService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async ValueTask<MemorySnapshot> ReadAsync(MemoryContext context, CancellationToken ct = default)
    {
        var paths = ResolvePaths(context);
        Directory.CreateDirectory(paths.WorkspaceMemoryDirectory);
        Directory.CreateDirectory(paths.UserMemoryDirectory);

        await EnsureFileAsync(paths.WorkspaceMemoryPath, "# QIANYUAN 工作空间长期记忆", ct).ConfigureAwait(false);
        await EnsureFileAsync(paths.UserMemoryPath, "# QIANYUAN 用户长期记忆", ct).ConfigureAwait(false);
        await EnsureFileAsync(paths.DailyLogPath, $"# {DateTime.Now:yyyy-MM-dd} 工作日志", ct).ConfigureAwait(false);

        return new MemorySnapshot(
            await ReadLimitedAsync(paths.UserMemoryPath, ct).ConfigureAwait(false),
            await ReadLimitedAsync(paths.WorkspaceMemoryPath, ct).ConfigureAwait(false),
            await ReadLimitedAsync(paths.DailyLogPath, ct).ConfigureAwait(false),
            paths.UserMemoryPath,
            paths.WorkspaceMemoryPath,
            paths.DailyLogPath);
    }

    public async ValueTask WriteMemoryAsync(MemoryContext context, string scope, string content, CancellationToken ct = default)
    {
        var text = content.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        var paths = ResolvePaths(context);
        var normalized = NormalizeScope(scope);
        var target = normalized switch
        {
            "user" => paths.UserMemoryPath,
            "daily" => paths.DailyLogPath,
            _ => paths.WorkspaceMemoryPath,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await EnsureFileAsync(target, normalized == "daily" ? $"# {DateTime.Now:yyyy-MM-dd} 工作日志" : "# QIANYUAN 记忆", ct).ConfigureAwait(false);

        if (normalized == "user" && text.Length > MaxUserWriteChars)
            text = text[..MaxUserWriteChars];

        var entry = normalized == "daily"
            ? $"\n- {DateTime.Now:HH:mm:ss} {text}\n"
            : $"\n\n## {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{text}\n";
        await AppendWithLockAsync(target, entry, ct).ConfigureAwait(false);
    }

    public async ValueTask AppendDailyLogAsync(MemoryContext context, string title, string? userText, string? assistantText, CancellationToken ct = default)
    {
        var paths = ResolvePaths(context);
        Directory.CreateDirectory(paths.WorkspaceMemoryDirectory);
        await EnsureFileAsync(paths.DailyLogPath, $"# {DateTime.Now:yyyy-MM-dd} 工作日志", ct).ConfigureAwait(false);

        var safeTitle = string.IsNullOrWhiteSpace(title) ? "未命名会话" : title.Trim();
        var user = Truncate(userText, MaxDailyTextChars);
        var assistant = Truncate(assistantText, MaxDailyTextChars);
        if (string.IsNullOrWhiteSpace(user) && string.IsNullOrWhiteSpace(assistant)) return;

        var entry = $"\n## {DateTime.Now:HH:mm:ss} {safeTitle}\n";
        if (!string.IsNullOrWhiteSpace(context.SessionId)) entry += $"- 会话：{context.SessionId}\n";
        if (!string.IsNullOrWhiteSpace(user)) entry += $"- 用户：{user}\n";
        if (!string.IsNullOrWhiteSpace(assistant)) entry += $"- 回复：{assistant}\n";
        await AppendWithLockAsync(paths.DailyLogPath, entry, ct).ConfigureAwait(false);
    }

    private MemoryPaths ResolvePaths(MemoryContext context)
    {
        var workspaceRoot = ResolveWorkspaceRoot(context.WorkspacePath);
        var workspaceMemoryDirectory = Path.Combine(workspaceRoot, ".qianyuan", "memory");
        var userRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userRoot)) userRoot = _environment.ContentRootPath;
        var userMemoryDirectory = Path.Combine(userRoot, ".qianyuan");

        return new MemoryPaths(
            workspaceMemoryDirectory,
            Path.Combine(workspaceMemoryDirectory, "MEMORY.md"),
            Path.Combine(workspaceMemoryDirectory, $"{DateTime.Now:yyyy-MM-dd}.md"),
            userMemoryDirectory,
            Path.Combine(userMemoryDirectory, "MEMORY.md"));
    }

    private string ResolveWorkspaceRoot(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            try
            {
                var full = Path.GetFullPath(requestedPath.Trim());
                if (Directory.Exists(full)) return full;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ignoring invalid workspace memory path {Path}", requestedPath);
            }
        }

        return _environment.ContentRootPath;
    }

    private static async ValueTask EnsureFileAsync(string path, string heading, CancellationToken ct)
    {
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, heading + Environment.NewLine, ct).ConfigureAwait(false);
    }

    private static async ValueTask<string?> ReadLimitedAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        if (text.Length <= MaxReadChars) return text;
        return text[^MaxReadChars..];
    }

    private static async ValueTask AppendWithLockAsync(string path, string content, CancellationToken ct)
    {
        var gate = FileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(path, content, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static string NormalizeScope(string? scope)
    {
        var value = (scope ?? "workspace").Trim().ToLowerInvariant();
        return value is "user" or "global" ? "user" : value is "daily" or "log" ? "daily" : "workspace";
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var compact = value.Trim().Replace("\r", " ").Replace("\n", " ");
        return compact.Length <= max ? compact : compact[..max] + "...";
    }

    private sealed record MemoryPaths(
        string WorkspaceMemoryDirectory,
        string WorkspaceMemoryPath,
        string DailyLogPath,
        string UserMemoryDirectory,
        string UserMemoryPath);
}
