using System.Text.Json;
using FluentAssertions;
using QianYuan.Core.Abstractions;
using QianYuan.Skills.Builtin.Code;

namespace QianYuan.Core.Tests;

public class CodeExecutionSkillSandboxTests
{
    [Fact]
    public async Task Code_run_uses_per_user_and_per_session_sandbox_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "qianyuan-code-sandbox-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var skill = new CodeExecutionSkill(new CodeExecutionOptions
            {
                SandboxDirectory = root,
                AllowedRuntimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bash" },
                PerCallTimeout = TimeSpan.FromSeconds(10),
                MaxOutputChars = 8000,
            });

            var context = new SkillInvocationContext
            {
                AgentId = "agent",
                SessionId = "session-A",
                Services = EmptyServices.Instance,
                Metadata = new Dictionary<string, string> { ["ownerId"] = "alice" }
            };

            var result = await skill.InvokeAsync(
                "code_run",
                JsonSerializer.Serialize(new { runtime = "bash", code = "pwd" }),
                context,
                CancellationToken.None);

            result.IsError.Should().BeFalse();

            using var doc = JsonDocument.Parse(result.JsonContent);
            var stdout = doc.RootElement.GetProperty("stdout").GetString() ?? string.Empty;
            stdout.Trim().Should().EndWith(Path.Combine("users", "alice", "sessions", "session-A"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Code_run_isolates_different_sessions_under_same_user()
    {
        var root = Path.Combine(Path.GetTempPath(), "qianyuan-code-sandbox-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var skill = new CodeExecutionSkill(new CodeExecutionOptions
            {
                SandboxDirectory = root,
                AllowedRuntimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bash" },
                PerCallTimeout = TimeSpan.FromSeconds(10),
                MaxOutputChars = 8000,
            });

            var resultA = await InvokePwdAsync(skill, ownerId: "alice", sessionId: "session-A");
            var resultB = await InvokePwdAsync(skill, ownerId: "alice", sessionId: "session-B");

            var pathA = ReadStdout(resultA).Trim();
            var pathB = ReadStdout(resultB).Trim();

            pathA.Should().NotBe(pathB);
            pathA.Should().EndWith(Path.Combine("users", "alice", "sessions", "session-A"));
            pathB.Should().EndWith(Path.Combine("users", "alice", "sessions", "session-B"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Code_run_sanitizes_owner_and_session_path_segments()
    {
        var root = Path.Combine(Path.GetTempPath(), "qianyuan-code-sandbox-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var skill = new CodeExecutionSkill(new CodeExecutionOptions
            {
                SandboxDirectory = root,
                AllowedRuntimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bash" },
                PerCallTimeout = TimeSpan.FromSeconds(10),
                MaxOutputChars = 8000,
            });

            var result = await InvokePwdAsync(skill, ownerId: "../a:b*?", sessionId: "session/..\\unsafe");
            var path = ReadStdout(result).Trim();
            var expectedRoot = Path.GetFullPath(root);

            path.Should().Contain(Path.Combine("users", "_a_b__", "sessions", "session_.._unsafe"));
            (path.StartsWith(expectedRoot, StringComparison.Ordinal)
                || path.StartsWith("/private" + expectedRoot, StringComparison.Ordinal))
                .Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task<SkillInvocationResult> InvokePwdAsync(CodeExecutionSkill skill, string ownerId, string sessionId)
    {
        var context = new SkillInvocationContext
        {
            AgentId = "agent",
            SessionId = sessionId,
            Services = EmptyServices.Instance,
            Metadata = new Dictionary<string, string> { ["ownerId"] = ownerId }
        };

        return await skill.InvokeAsync(
            "code_run",
            JsonSerializer.Serialize(new { runtime = "bash", code = "pwd" }),
            context,
            CancellationToken.None);
    }

    private static string ReadStdout(SkillInvocationResult result)
    {
        result.IsError.Should().BeFalse(result.JsonContent);
        using var doc = JsonDocument.Parse(result.JsonContent);
        return doc.RootElement.GetProperty("stdout").GetString() ?? string.Empty;
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public static readonly EmptyServices Instance = new();
        public object? GetService(Type serviceType) => null;
    }
}