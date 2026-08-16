using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;
using QianYuan.Core.Sandbox;

namespace QianYuan.Skills.Builtin.Code;

/// <summary>
/// Code execution skill. Spawns a child process for an allowed interpreter (python/node/dotnet-script)
/// inside a sandbox directory with a per-call timeout. Disabled by default - host opts in explicitly.
/// </summary>
public sealed class CodeExecutionSkill : ISkill
{
    private readonly CodeExecutionOptions _opts;
    private const string AnonymousOwner = "anonymous";

    public CodeExecutionSkill(CodeExecutionOptions opts)
    {
        _opts = opts;
        Directory.CreateDirectory(_opts.SandboxDirectory);
    }

    public string Id => "qianyuan.code";
    public string Name => "Code Execution";
    public string Description => $"Run snippets of {string.Join("/", _opts.AllowedRuntimes)} inside a sandbox.";
    public IReadOnlyList<string> Tags => new[] { "code", "exec", "python", "compute", "calculate" };
    public string? SystemPromptFragment => "Use code_run for computation, data manipulation, or quick scripting. Code runs in a sandboxed temp directory.";

    public ValueTask<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct = default)
    {
        var enumList = string.Join(",", _opts.AllowedRuntimes.Select(r => $"\"{r}\""));
        ToolDefinition[] tools =
        [
            new ToolDefinition
            {
                Name = "code_run",
                Description = "Run a code snippet. Returns stdout, stderr, exit code.",
                JsonSchema = "{\"type\":\"object\",\"properties\":{\"runtime\":{\"type\":\"string\",\"enum\":[" + enumList + "]},\"code\":{\"type\":\"string\"}},\"required\":[\"runtime\",\"code\"]}"
            }
        ];
        return ValueTask.FromResult<IReadOnlyList<ToolDefinition>>(tools);
    }

    public async ValueTask<SkillInvocationResult> InvokeAsync(string toolName, string argumentsJson, SkillInvocationContext context, CancellationToken ct = default)
    {
        if (toolName != "code_run") return SkillInvocationResult.Error($"unknown tool '{toolName}'");

        var args = JsonNode.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson) ?? new JsonObject();
        var runtime = args["runtime"]?.GetValue<string>();
        var code = args["code"]?.GetValue<string>();
        if (string.IsNullOrEmpty(runtime) || !_opts.AllowedRuntimes.Contains(runtime))
            return SkillInvocationResult.Error($"runtime '{runtime}' not allowed");
        if (string.IsNullOrEmpty(code)) return SkillInvocationResult.Error("'code' is required");

        var (fileName, suffix) = runtime switch
        {
            "python" => ("python3", ".py"),
            "node" => ("node", ".js"),
            "bash" => ("bash", ".sh"),
            _ => ("", "")
        };
        if (string.IsNullOrEmpty(fileName)) return SkillInvocationResult.Error($"unsupported runtime '{runtime}'");

        var sandboxDirectory = ResolveInvocationSandboxDirectory(context);
        Directory.CreateDirectory(sandboxDirectory);

        var workerClient = context.Services.GetService<ICodeExecutionWorkerClient>();
        if (workerClient is not null)
        {
            var workerRequest = new CodeExecutionWorkerRequest
            {
                Runtime = runtime,
                Code = code,
                WorkingDirectory = sandboxDirectory,
                Timeout = _opts.PerCallTimeout,
                MaxOutputChars = _opts.MaxOutputChars,
                LeaseId = context.SandboxPolicy?.LeaseId,
                SessionId = context.SessionId,
                OwnerId = context.SandboxPolicy?.OwnerId,
                WorkspaceId = context.SandboxPolicy?.WorkspaceId,
            };

            var workerResult = await workerClient.ExecuteAsync(workerRequest, ct).ConfigureAwait(false);
            if (!workerResult.Succeeded)
            {
                var failure = workerResult.ErrorMessage
                    ?? (workerResult.TimedOut
                        ? $"execution timed out after {_opts.PerCallTimeout}"
                        : "worker execution failed");
                return SkillInvocationResult.Error(failure);
            }

            var payload = JsonSerializer.Serialize(new
            {
                exitCode = workerResult.ExitCode,
                stdout = workerResult.Stdout,
                stderr = workerResult.Stderr,
                workerId = workerResult.WorkerId,
                attempt = workerResult.Attempt,
                durationMs = workerResult.DurationMs,
            });
            var summary = workerResult.ExitCode == 0
                ? $"ran ok ({workerResult.Stdout.Length} bytes stdout)"
                : $"exit {workerResult.ExitCode}";
            return SkillInvocationResult.Ok(payload, summary);
        }

        var temp = Path.Combine(sandboxDirectory, $"snippet_{Guid.NewGuid():N}{suffix}");
        await File.WriteAllTextAsync(temp, code, ct).ConfigureAwait(false);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_opts.PerCallTimeout);

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = sandboxDirectory,
            };
            psi.ArgumentList.Add(temp);

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start {fileName}");
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            var payload = JsonSerializer.Serialize(new
            {
                exitCode = proc.ExitCode,
                stdout = Truncate(stdout, _opts.MaxOutputChars),
                stderr = Truncate(stderr, _opts.MaxOutputChars),
            });
            var summary = proc.ExitCode == 0 ? $"ran ok ({stdout.Length} bytes stdout)" : $"exit {proc.ExitCode}";
            return SkillInvocationResult.Ok(payload, summary);
        }
        catch (OperationCanceledException)
        {
            return SkillInvocationResult.Error($"execution timed out after {_opts.PerCallTimeout}");
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    private string ResolveInvocationSandboxDirectory(SkillInvocationContext context)
    {
        var policy = context.SandboxPolicy;
        if (!string.IsNullOrWhiteSpace(policy?.LeaseDirectory))
        {
            var leaseDir = Path.GetFullPath(policy.LeaseDirectory);
            var sandboxRoot = Path.GetFullPath(_opts.SandboxDirectory);
            if (!leaseDir.StartsWith(sandboxRoot, StringComparison.Ordinal))
                throw new InvalidOperationException("resolved lease path escapes configured root");

            return leaseDir;
        }

        var ownerId = policy?.OwnerId
            ?? ResolveMetadataValue(context, "ownerId")
            ?? AnonymousOwner;

        var workspaceKey = policy?.WorkspaceId
            ?? policy?.WorkspaceLabel
            ?? policy?.WorkspaceRoot
            ?? ResolveMetadataValue(context, "workspaceId")
            ?? ResolveMetadataValue(context, "workspaceLabel")
            ?? ResolveMetadataValue(context, "workspacePath")
            ?? "default-workspace";

        var safeOwner = SanitizeSegment(ownerId);
        var safeWorkspace = SanitizeSegment(workspaceKey);
        var safeSession = SanitizeSegment(context.SessionId);
        var root = Path.GetFullPath(_opts.SandboxDirectory);
        var scoped = Path.Combine(root, "users", safeOwner, "workspaces", safeWorkspace, "sessions", safeSession);
        var scopedFull = Path.GetFullPath(scoped);

        if (!scopedFull.StartsWith(root, StringComparison.Ordinal))
            throw new InvalidOperationException("resolved sandbox path escapes configured root");

        return scopedFull;
    }

    private static string? ResolveMetadataValue(SkillInvocationContext context, string key)
        => context.Metadata is not null && context.Metadata.TryGetValue(key, out var value) ? value : null;

    private static string SanitizeSegment(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return "unknown";

        var builder = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.') builder.Append(ch);
            else builder.Append('_');
        }

        var sanitized = builder.ToString().Trim('.');
        if (sanitized.Length == 0) sanitized = "unknown";
        return sanitized.Length > 80 ? sanitized[..80] : sanitized;
    }

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] + "...[truncated]" : s;
}

public sealed class CodeExecutionOptions
{
    public required string SandboxDirectory { get; init; }
    public IReadOnlySet<string> AllowedRuntimes { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "python", "node" };
    public TimeSpan PerCallTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public int MaxOutputChars { get; init; } = 8000;
}
