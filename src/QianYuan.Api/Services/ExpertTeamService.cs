using System.Text;
using Microsoft.EntityFrameworkCore;
using QianYuan.Api.Models;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;
using QianYuan.Core.Streaming;
using QianYuan.Data;
using QianYuan.Data.Entities;

namespace QianYuan.Api.Services;

public interface IExpertTeamService
{
    Task<IReadOnlyList<ExpertTeamDto>> ListAsync(Guid userId, CancellationToken ct = default);
    Task<ExpertTeamDto?> GetAsync(Guid userId, Guid teamId, CancellationToken ct = default);
    Task<ExpertTeamDto> CreateAsync(Guid userId, CreateExpertTeamRequest request, CancellationToken ct = default);
    Task<WorkTaskDetailDto> OrchestrateTaskAsync(Guid userId, Guid taskId, Guid? teamId, CancellationToken ct = default);
    Task<WorkTaskDetailDto> ExecuteTaskAsync(Guid userId, Guid taskId, Guid? teamId, int? maxIterations, int? timeoutSeconds, CancellationToken ct = default);
}

public sealed class ExpertTeamService : IExpertTeamService
{
    private readonly QianYuanDbContext _db;
    private readonly IAgentRegistry _agents;
    private readonly IWorkTaskService _workTasks;
    private readonly ICreditService _credits;

    public ExpertTeamService(QianYuanDbContext db, IAgentRegistry agents, IWorkTaskService workTasks, ICreditService credits)
    {
        _db = db;
        _agents = agents;
        _workTasks = workTasks;
        _credits = credits;
    }

    public async Task<IReadOnlyList<ExpertTeamDto>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        await EnsureDefaultTeamsAsync(userId, ct);
        return await _db.ExpertTeams
            .AsNoTracking()
            .Include(t => t.Members.OrderBy(m => m.MemberOrder))
            .AsSplitQuery()
            .Where(t => t.UserId == userId && t.Enabled)
            .OrderBy(t => t.CreatedAt)
            .Select(t => ToDto(t))
            .ToListAsync(ct);
    }

    public async Task<ExpertTeamDto?> GetAsync(Guid userId, Guid teamId, CancellationToken ct = default)
    {
        await EnsureDefaultTeamsAsync(userId, ct);
        var team = await _db.ExpertTeams
            .AsNoTracking()
            .Include(t => t.Members.OrderBy(m => m.MemberOrder))
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == teamId && t.UserId == userId, ct);
        return team is null ? null : ToDto(team);
    }

    public async Task<ExpertTeamDto> CreateAsync(Guid userId, CreateExpertTeamRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Team name is required.");
        var fallbackAgentId = GetFallbackAgentId();
        var now = DateTime.UtcNow;
        var team = new ExpertTeam
        {
            UserId = userId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            Scenario = string.IsNullOrWhiteSpace(request.Scenario) ? "custom" : request.Scenario.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        var members = request.Members?.Count > 0 ? request.Members : [
            new CreateExpertTeamMemberRequest("coordinator", "主控专家", fallbackAgentId, "理解目标、拆分步骤、协调专家输出。", "Sequential")
        ];
        var order = 1;
        foreach (var member in members)
        {
            team.Members.Add(new ExpertTeamMember
            {
                UserId = userId,
                MemberOrder = order++,
                RoleId = Normalize(member.RoleId, "expert"),
                DisplayName = Normalize(member.DisplayName, "专家"),
                AgentId = Normalize(member.AgentId, fallbackAgentId),
                Responsibility = Normalize(member.Responsibility, "完成分配的专业任务。"),
                ExecutionMode = Normalize(member.ExecutionMode, "Sequential"),
            });
        }

        _db.ExpertTeams.Add(team);
        await _db.SaveChangesAsync(ct);
        return (await GetAsync(userId, team.Id, ct))!;
    }

    public async Task<WorkTaskDetailDto> OrchestrateTaskAsync(Guid userId, Guid taskId, Guid? teamId, CancellationToken ct = default)
    {
        await EnsureDefaultTeamsAsync(userId, ct);
        var task = await _db.WorkTasks.FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Work task not found.");
        var team = await ResolveTeamAsync(userId, teamId, task.TeamId, ct);
        var members = await _db.ExpertTeamMembers
            .Where(m => m.TeamId == team.Id && m.UserId == userId && m.Enabled)
            .OrderBy(m => m.MemberOrder)
            .ToListAsync(ct);

        var existingMaxOrder = await _db.WorkSteps
            .Where(s => s.TaskId == task.Id)
            .Select(s => (int?)s.StepOrder)
            .MaxAsync(ct) ?? 0;
        foreach (var member in members)
        {
            _db.WorkSteps.Add(new WorkStep
            {
                TaskId = task.Id,
                UserId = userId,
                StepOrder = ++existingMaxOrder,
                Name = member.DisplayName,
                Status = "Planned",
                AgentId = member.AgentId,
                Summary = member.Responsibility,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }

        var content = BuildPlanArtifact(task, team, members);
        _db.WorkArtifacts.Add(new WorkArtifact
        {
            TaskId = task.Id,
            UserId = userId,
            Name = "专家团编排.md",
            ContentType = "text/markdown",
            StorageKind = "Database",
            Content = content,
            SizeBytes = Encoding.UTF8.GetByteCount(content),
            CreatedAt = DateTime.UtcNow,
        });
        task.TeamId = team.Id.ToString();
        task.Status = "Planned";
        task.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return (await _workTasks.GetAsync(userId, task.Id, ct))!;
    }

    public async Task<WorkTaskDetailDto> ExecuteTaskAsync(Guid userId, Guid taskId, Guid? teamId, int? maxIterations, int? timeoutSeconds, CancellationToken ct = default)
    {
        await EnsureDefaultTeamsAsync(userId, ct);
        var task = await _db.WorkTasks.FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Work task not found.");
        var team = await ResolveTeamAsync(userId, teamId, task.TeamId, ct);

        var memberStepCount = await _db.WorkSteps.CountAsync(s => s.TaskId == task.Id && s.UserId == userId && s.AgentId != null, ct);
        if (memberStepCount == 0)
        {
            await OrchestrateTaskAsync(userId, task.Id, team.Id, ct);
            task = await _db.WorkTasks.FirstAsync(t => t.Id == taskId && t.UserId == userId, ct);
        }

        var steps = await _db.WorkSteps
            .Where(s => s.TaskId == task.Id && s.UserId == userId && s.AgentId != null && (s.Status == "Planned" || s.Status == "Pending"))
            .OrderBy(s => s.StepOrder)
            .ToListAsync(ct);
        if (steps.Count == 0) return (await _workTasks.GetAsync(userId, task.Id, ct))!;

        task.TeamId = team.Id.ToString();
        task.Status = "Running";
        task.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var completedOutputs = (await LoadCompletedOutputsAsync(userId, task.Id, ct)).ToList();
        var stepTimeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds ?? 60, 5, 600));
        foreach (var step in steps)
        {
            step.Status = "Running";
            step.UpdatedAt = DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            var agent = ResolveAgent(step.AgentId);
            if (agent is null)
            {
                await MarkExecutionFailureAsync(task, step, "没有可用 Agent 执行该专家步骤。", ct);
                return (await _workTasks.GetAsync(userId, task.Id, ct))!;
            }

            try
            {
                using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stepCts.CancelAfter(stepTimeout);
                var result = await RunMemberAgentAsync(userId, task, team, step, agent, completedOutputs, maxIterations, stepCts.Token);
                var normalizedOutput = string.IsNullOrWhiteSpace(result.Output) ? "Agent 未返回文本结果。" : result.Output.Trim();
                var consumedCredits = await ConsumeStepCreditsAsync(userId, task, team, step, result, ct);
                step.Status = "Completed";
                step.Summary = Snippet($"{normalizedOutput}\n\nCredits: -{consumedCredits}", 500);
                step.UpdatedAt = DateTime.UtcNow;
                var outputWithBilling = AppendBillingSummary(normalizedOutput, result, consumedCredits);
                AddArtifact(task, userId, $"{step.StepOrder:00}-{step.Name}-输出.md", outputWithBilling);
                completedOutputs.Add($"## {step.StepOrder}. {step.Name}\n\n{outputWithBilling}");
                task.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await MarkExecutionFailureAsync(task, step, $"专家步骤执行超过 {stepTimeout.TotalSeconds:0} 秒，已自动停止。", ct);
                return (await _workTasks.GetAsync(userId, task.Id, ct))!;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await MarkExecutionFailureAsync(task, step, ex.Message, ct);
                return (await _workTasks.GetAsync(userId, task.Id, ct))!;
            }
        }

        var report = BuildExecutionReport(task, team, completedOutputs);
        AddArtifact(task, userId, "专家团执行报告.md", report);
        task.Status = "Completed";
        task.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return (await _workTasks.GetAsync(userId, task.Id, ct))!;
    }

    private async Task EnsureDefaultTeamsAsync(Guid userId, CancellationToken ct)
    {
        if (await _db.ExpertTeams.AnyAsync(t => t.UserId == userId, ct)) return;
        var agentId = GetFallbackAgentId();
        await CreateAsync(userId, new CreateExpertTeamRequest("研发交付专家团", "从需求澄清到测试建议的研发任务协同团队。", "software-delivery", [
            new("product", "产品专家", agentId, "澄清目标、定义范围、提炼验收标准。", "Sequential"),
            new("architect", "架构专家", agentId, "设计方案、拆分模块、识别技术风险。", "Sequential"),
            new("developer", "研发专家", agentId, "生成实现方案、代码修改建议和交付清单。", "Sequential"),
            new("qa", "测试专家", agentId, "设计测试点、验证风险和回归建议。", "Sequential"),
        ]), ct);
        await CreateAsync(userId, new CreateExpertTeamRequest("深度研究专家团", "面向行业、竞品和专题调研的并行分析团队。", "research", [
            new("coordinator", "研究主控", agentId, "拆解研究主题、协调并汇总专家观点。", "Sequential"),
            new("market", "市场专家", agentId, "分析市场空间、用户画像和商业趋势。", "Parallel"),
            new("competitor", "竞品专家", agentId, "对比竞品能力、定位和差异化机会。", "Parallel"),
            new("writer", "报告专家", agentId, "整理结构化报告和可执行建议。", "Sequential"),
        ]), ct);
    }

    private async Task<ExpertTeam> ResolveTeamAsync(Guid userId, Guid? teamId, string? taskTeamId, CancellationToken ct)
    {
        var resolvedId = teamId;
        if (resolvedId is null && Guid.TryParse(taskTeamId, out var parsed)) resolvedId = parsed;
        if (resolvedId is not null)
        {
            var selected = await _db.ExpertTeams.FirstOrDefaultAsync(t => t.Id == resolvedId && t.UserId == userId, ct);
            if (selected is not null) return selected;
        }
        return await _db.ExpertTeams.OrderBy(t => t.CreatedAt).FirstAsync(t => t.UserId == userId && t.Enabled, ct);
    }

    private string GetFallbackAgentId() => _agents.List().FirstOrDefault()?.Id ?? "qianyuan.default";

    private IAgent? ResolveAgent(string? agentId)
    {
        if (!string.IsNullOrWhiteSpace(agentId))
        {
            var agent = _agents.Get(agentId);
            if (agent is not null) return agent;
        }
        return _agents.List().FirstOrDefault();
    }

    private static ExpertTeamDto ToDto(ExpertTeam team)
    {
        return new ExpertTeamDto(team.Id, team.Name, team.Description, team.Scenario, team.Enabled, team.CreatedAt, team.UpdatedAt,
            team.Members.OrderBy(m => m.MemberOrder).Select(m => new ExpertTeamMemberDto(m.Id, m.MemberOrder, m.RoleId, m.DisplayName, m.AgentId, m.Responsibility, m.ExecutionMode, m.Enabled)).ToList());
    }

    private static string BuildPlanArtifact(WorkTask task, ExpertTeam team, IReadOnlyList<ExpertTeamMember> members)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {task.Title} - 专家团编排");
        sb.AppendLine();
        sb.AppendLine($"专家团：{team.Name}");
        sb.AppendLine();
        sb.AppendLine("## 协同步骤");
        sb.AppendLine();
        foreach (var member in members)
        {
            sb.AppendLine($"{member.MemberOrder}. **{member.DisplayName}** ({member.ExecutionMode})");
            sb.AppendLine($"   - Agent: `{member.AgentId}`");
            sb.AppendLine($"   - 职责：{member.Responsibility}");
        }
        sb.AppendLine();
        sb.AppendLine("## 下一步");
        sb.AppendLine();
        sb.AppendLine("进入执行阶段后，主控 Agent 将按以上角色顺序/并行模式调度专家产出，并把结果沉淀到任务产物区。");
        return sb.ToString();
    }

    private async Task<MemberAgentResult> RunMemberAgentAsync(
        Guid userId,
        WorkTask task,
        ExpertTeam team,
        WorkStep step,
        IAgent agent,
        IReadOnlyList<string> completedOutputs,
        int? maxIterations,
        CancellationToken ct)
    {
        var prompt = BuildMemberPrompt(task, team, step, completedOutputs);
        var run = new AgentRunRequest
        {
            Messages = [ChatMessage.User(prompt)],
            SessionId = $"worktask-{task.Id:N}-{step.Id:N}",
            ProviderOverride = task.ProviderId,
            ModelOverride = task.Model,
            MaxIterations = maxIterations is > 0 ? maxIterations : 8,
            Metadata = new Dictionary<string, string>
            {
                ["ownerId"] = userId.ToString(),
                ["workTaskId"] = task.Id.ToString(),
                ["expertTeamId"] = team.Id.ToString(),
                ["workStepId"] = step.Id.ToString(),
            },
        };

        var output = new StringBuilder();
        TokenUsage? usage = null;
        await foreach (var chunk in agent.RunAsync(run, ct).ConfigureAwait(false))
        {
            if (chunk.Kind == StreamingChunkKind.TextDelta && chunk.Text is not null)
            {
                output.Append(chunk.Text);
            }
            else if (chunk.Kind == StreamingChunkKind.Warning && chunk.Text is not null)
            {
                output.AppendLine().AppendLine($"> Warning: {chunk.Text}");
            }
            else if (chunk.Kind == StreamingChunkKind.Error && chunk.Text is not null)
            {
                output.AppendLine().AppendLine($"> Error: {chunk.Text}");
            }
            if (chunk.Usage is not null) usage = chunk.Usage;
        }
        return new MemberAgentResult(output.ToString(), usage, EstimateTokens(prompt), EstimateTokens(output.ToString()));
    }

    private static string BuildMemberPrompt(WorkTask task, ExpertTeam team, WorkStep step, IReadOnlyList<string> completedOutputs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"你正在 WorkPartner 的专家团任务中担任：{step.Name}");
        sb.AppendLine($"专家团：{team.Name}");
        sb.AppendLine($"任务标题：{task.Title}");
        sb.AppendLine();
        sb.AppendLine("## 总目标");
        sb.AppendLine(task.Goal);
        sb.AppendLine();
        sb.AppendLine("## 当前专家职责");
        sb.AppendLine(step.Summary ?? "完成当前专家角色的专业分析与交付。");
        if (completedOutputs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## 前序专家输出");
            foreach (var item in completedOutputs) sb.AppendLine(item).AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("请只输出当前专家的结构化交付内容，使用 Markdown，包含关键结论、推理依据、风险/假设和下一步建议。不要解释系统流程。用中文回答。 ");
        return sb.ToString();
    }

    private async Task<IReadOnlyList<string>> LoadCompletedOutputsAsync(Guid userId, Guid taskId, CancellationToken ct)
    {
        return await _db.WorkArtifacts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.TaskId == taskId && a.Name.EndsWith("-输出.md"))
            .OrderBy(a => a.CreatedAt)
            .Select(a => a.Content ?? string.Empty)
            .ToListAsync(ct);
    }

    private async Task MarkExecutionFailureAsync(WorkTask task, WorkStep step, string message, CancellationToken ct)
    {
        var normalized = string.IsNullOrWhiteSpace(message) ? "执行失败，未返回详细错误。" : message.Trim();
        step.Status = "Failed";
        step.Summary = Snippet(normalized, 500);
        step.UpdatedAt = DateTime.UtcNow;
        task.Status = "Failed";
        task.UpdatedAt = DateTime.UtcNow;
        AddArtifact(task, task.UserId, $"{step.StepOrder:00}-{step.Name}-执行失败.md", $"# {step.Name} 执行失败\n\n{normalized}\n");
        await _db.SaveChangesAsync(ct);
    }

    private async Task<long> ConsumeStepCreditsAsync(Guid userId, WorkTask task, ExpertTeam team, WorkStep step, MemberAgentResult result, CancellationToken ct)
    {
        var inputTokens = result.Usage?.InputTokens ?? result.EstimatedInputTokens;
        var outputTokens = result.Usage?.OutputTokens ?? result.EstimatedOutputTokens;
        var estimate = _credits.Estimate(new EstimateCreditsRequest(inputTokens, outputTokens, ResolveModelTier(task.ProviderId, task.Model), "expert-team"));
        var amount = Math.Max(1, estimate.EstimatedCredits);
        await _credits.ConsumeAsync(userId, new ConsumeCreditsRequest(
            amount,
            "WorkTask",
            task.Id.ToString(),
            $"专家团执行：{team.Name} / {step.Name}"), ct);
        return amount;
    }

    private static string AppendBillingSummary(string content, MemberAgentResult result, long consumedCredits)
    {
        var inputTokens = result.Usage?.InputTokens ?? result.EstimatedInputTokens;
        var outputTokens = result.Usage?.OutputTokens ?? result.EstimatedOutputTokens;
        var usageKind = result.Usage is null ? "estimated" : "actual";
        var sb = new StringBuilder(content.TrimEnd());
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("## Credits");
        sb.AppendLine();
        sb.AppendLine($"- Consumed: {consumedCredits}");
        sb.AppendLine($"- Usage: {usageKind}");
        sb.AppendLine($"- Input tokens: {inputTokens}");
        sb.AppendLine($"- Output tokens: {outputTokens}");
        return sb.ToString();
    }

    private void AddArtifact(WorkTask task, Guid userId, string name, string content)
    {
        _db.WorkArtifacts.Add(new WorkArtifact
        {
            TaskId = task.Id,
            UserId = userId,
            Name = name,
            ContentType = "text/markdown",
            StorageKind = "Database",
            Content = content,
            SizeBytes = Encoding.UTF8.GetByteCount(content),
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static string BuildExecutionReport(WorkTask task, ExpertTeam team, IReadOnlyList<string> completedOutputs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {task.Title} - 专家团执行报告");
        sb.AppendLine();
        sb.AppendLine($"专家团：{team.Name}");
        sb.AppendLine($"执行完成时间：{DateTime.UtcNow:O}");
        sb.AppendLine();
        sb.AppendLine("## 汇总");
        sb.AppendLine();
        if (completedOutputs.Count == 0)
        {
            sb.AppendLine("暂无专家输出。");
        }
        else
        {
            foreach (var item in completedOutputs) sb.AppendLine(item).AppendLine();
        }
        return sb.ToString();
    }

    private static string Snippet(string value, int maxLength)
    {
        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return Math.Max(1, (int)Math.Ceiling(text.Length / 4m));
    }

    private static string ResolveModelTier(string? providerId, string? model)
    {
        var value = $"{providerId} {model}".ToLowerInvariant();
        if (value.Contains("opus") || value.Contains("gpt-5") || value.Contains("o1") || value.Contains("premium")) return "premium";
        if (value.Contains("gpt-4") || value.Contains("claude") || value.Contains("gemini") || value.Contains("qwen-max")) return "advanced";
        return "basic";
    }

    private static string Normalize(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private sealed record MemberAgentResult(string Output, TokenUsage? Usage, int EstimatedInputTokens, int EstimatedOutputTokens);
}