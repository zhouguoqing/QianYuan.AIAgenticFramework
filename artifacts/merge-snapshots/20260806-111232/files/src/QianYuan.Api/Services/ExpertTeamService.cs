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
    Task<ExpertTeamDto> CreateFromTemplateAsync(Guid userId, string templateId, CancellationToken ct = default);
    Task<ExpertTeamDto> UpdateAsync(Guid userId, Guid teamId, UpdateExpertTeamRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid teamId, CancellationToken ct = default);
    Task<ExpertTeamMemberDto> AddMemberAsync(Guid userId, Guid teamId, CreateExpertTeamMemberRequest request, CancellationToken ct = default);
    Task<ExpertTeamMemberDto> UpdateMemberAsync(Guid userId, Guid teamId, Guid memberId, UpdateExpertTeamMemberRequest request, CancellationToken ct = default);
    Task DeleteMemberAsync(Guid userId, Guid teamId, Guid memberId, CancellationToken ct = default);
    Task<WorkTaskDetailDto> OrchestrateTaskAsync(Guid userId, Guid taskId, Guid? teamId, CancellationToken ct = default);
    Task<WorkTaskDetailDto> ExecuteTaskAsync(Guid userId, Guid taskId, Guid? teamId, int? maxIterations, int? timeoutSeconds, Func<ExpertTeamExecutionEventDto, CancellationToken, Task>? onEvent = null, CancellationToken ct = default);
}

public sealed partial class ExpertTeamService : IExpertTeamService
{
    private readonly QianYuanDbContext _db;
    private readonly IAgentRegistry _agents;
    private readonly IWorkTaskService _workTasks;
    private readonly ICreditService _credits;
    private readonly IExpertTeamTemplateService _templates;

    public ExpertTeamService(QianYuanDbContext db, IAgentRegistry agents, IWorkTaskService workTasks, ICreditService credits, IExpertTeamTemplateService templates)
    {
        _db = db;
        _agents = agents;
        _workTasks = workTasks;
        _credits = credits;
        _templates = templates;
    }

    public async Task<IReadOnlyList<ExpertTeamDto>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        await EnsureDefaultTeamsAsync(userId, ct);
        return await _db.ExpertTeams.AsNoTracking()
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
        var team = await _db.ExpertTeams.AsNoTracking()
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
        var members = request.Members?.Count > 0
            ? request.Members
            : [new CreateExpertTeamMemberRequest("coordinator", "QIANYUAN Coordinator", fallbackAgentId, "Coordinate goals, split work, and consolidate expert outputs.", "Sequential")];
        var order = 1;
        foreach (var item in members)
        {
            team.Members.Add(new ExpertTeamMember
            {
                UserId = userId,
                MemberOrder = order++,
                RoleId = Normalize(item.RoleId, "expert"),
                DisplayName = Normalize(item.DisplayName, "QIANYUAN Expert"),
                AgentId = Normalize(item.AgentId, fallbackAgentId),
                Responsibility = Normalize(item.Responsibility, "Complete the assigned expert task."),
                ExecutionMode = NormalizeExecutionMode(item.ExecutionMode),
                Enabled = true,
            });
        }
        _db.ExpertTeams.Add(team);
        await _db.SaveChangesAsync(ct);
        return (await GetAsync(userId, team.Id, ct))!;
    }

    public async Task<ExpertTeamDto> CreateFromTemplateAsync(Guid userId, string templateId, CancellationToken ct = default)
    {
        var template = _templates.GetTemplate(templateId) ?? throw new KeyNotFoundException("Expert team template not found.");
        var fallbackAgentId = GetFallbackAgentId();
        var request = new CreateExpertTeamRequest(
            template.Name,
            template.Description,
            template.Scenario,
            template.Members.Select(m => new CreateExpertTeamMemberRequest(m.RoleId, m.DisplayName, fallbackAgentId, m.Responsibility, m.ExecutionMode)).ToList());
        return await CreateAsync(userId, request, ct);
    }

    public async Task<ExpertTeamDto> UpdateAsync(Guid userId, Guid teamId, UpdateExpertTeamRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Team name is required.");
        var team = await _db.ExpertTeams.FirstOrDefaultAsync(t => t.Id == teamId && t.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Expert team not found.");
        team.Name = request.Name.Trim();
        team.Description = request.Description?.Trim() ?? string.Empty;
        team.Scenario = string.IsNullOrWhiteSpace(request.Scenario) ? "custom" : request.Scenario.Trim();
        if (request.Enabled is not null) team.Enabled = request.Enabled.Value;
        team.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return (await GetAsync(userId, team.Id, ct))!;
    }

    public async Task DeleteAsync(Guid userId, Guid teamId, CancellationToken ct = default)
    {
        var team = await _db.ExpertTeams.FirstOrDefaultAsync(t => t.Id == teamId && t.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Expert team not found.");
        team.Enabled = false;
        team.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ExpertTeamMemberDto> AddMemberAsync(Guid userId, Guid teamId, CreateExpertTeamMemberRequest request, CancellationToken ct = default)
    {
        var team = await _db.ExpertTeams.Include(t => t.Members).FirstOrDefaultAsync(t => t.Id == teamId && t.UserId == userId && t.Enabled, ct)
            ?? throw new KeyNotFoundException("Expert team not found.");
        var fallbackAgentId = GetFallbackAgentId();
        var order = team.Members.Count == 0 ? 1 : team.Members.Max(m => m.MemberOrder) + 1;
        var member = new ExpertTeamMember
        {
            TeamId = team.Id,
            UserId = userId,
            MemberOrder = order,
            RoleId = Normalize(request.RoleId, "expert"),
            DisplayName = Normalize(request.DisplayName, "QIANYUAN Expert"),
            AgentId = Normalize(request.AgentId, fallbackAgentId),
            Responsibility = Normalize(request.Responsibility, "Complete the assigned expert task."),
            ExecutionMode = NormalizeExecutionMode(request.ExecutionMode),
            Enabled = true,
        };
        team.UpdatedAt = DateTime.UtcNow;
        _db.ExpertTeamMembers.Add(member);
        await _db.SaveChangesAsync(ct);
        return ToDto(member);
    }

    public async Task<ExpertTeamMemberDto> UpdateMemberAsync(Guid userId, Guid teamId, Guid memberId, UpdateExpertTeamMemberRequest request, CancellationToken ct = default)
    {
        var team = await _db.ExpertTeams.FirstOrDefaultAsync(t => t.Id == teamId && t.UserId == userId && t.Enabled, ct)
            ?? throw new KeyNotFoundException("Expert team not found.");
        var member = await _db.ExpertTeamMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.TeamId == teamId && m.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Expert team member not found.");
        if (request.MemberOrder is not null) member.MemberOrder = Math.Max(1, request.MemberOrder.Value);
        member.RoleId = Normalize(request.RoleId, "expert");
        member.DisplayName = Normalize(request.DisplayName, "QIANYUAN Expert");
        member.AgentId = Normalize(request.AgentId, GetFallbackAgentId());
        member.Responsibility = Normalize(request.Responsibility, "Complete the assigned expert task.");
        member.ExecutionMode = NormalizeExecutionMode(request.ExecutionMode);
        if (request.Enabled is not null) member.Enabled = request.Enabled.Value;
        team.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToDto(member);
    }

    public async Task DeleteMemberAsync(Guid userId, Guid teamId, Guid memberId, CancellationToken ct = default)
    {
        var team = await _db.ExpertTeams.FirstOrDefaultAsync(t => t.Id == teamId && t.UserId == userId && t.Enabled, ct)
            ?? throw new KeyNotFoundException("Expert team not found.");
        var member = await _db.ExpertTeamMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.TeamId == teamId && m.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Expert team member not found.");
        _db.ExpertTeamMembers.Remove(member);
        team.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<WorkTaskDetailDto> OrchestrateTaskAsync(Guid userId, Guid taskId, Guid? teamId, CancellationToken ct = default)
    {
        var task = await _db.WorkTasks.Include(t => t.Steps).Include(t => t.Artifacts).FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Work task not found.");
        var team = await ResolveTeamAsync(userId, teamId, task.TeamId, ct);
        var members = team.Members.Where(m => m.Enabled).OrderBy(m => m.MemberOrder).ToList();
        if (members.Count == 0) throw new InvalidOperationException("Expert team has no enabled members.");

        _db.WorkSteps.RemoveRange(task.Steps);
        task.TeamId = team.Id.ToString();
        task.Status = "Ready";
        task.UpdatedAt = DateTime.UtcNow;
        var order = 1;
        foreach (var member in members)
        {
            task.Steps.Add(new WorkStep
            {
                UserId = userId,
                StepOrder = order++,
                Name = member.DisplayName,
                Status = "Pending",
                AgentId = member.AgentId,
                Summary = member.Responsibility,
                ExecutionMode = NormalizeExecutionMode(member.ExecutionMode),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        AddArtifact(task, userId, "expert-team-plan.md", BuildPlanArtifact(task, team, members));
        await _db.SaveChangesAsync(ct);
        return (await _workTasks.GetAsync(userId, taskId, ct))!;
    }

    public async Task<WorkTaskDetailDto> ExecuteTaskAsync(Guid userId, Guid taskId, Guid? teamId, int? maxIterations, int? timeoutSeconds, Func<ExpertTeamExecutionEventDto, CancellationToken, Task>? onEvent = null, CancellationToken ct = default)
    {
        var task = await _db.WorkTasks.Include(t => t.Steps.OrderBy(s => s.StepOrder)).Include(t => t.Artifacts).FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Work task not found.");
        var team = await ResolveTeamAsync(userId, teamId, task.TeamId, ct);
        var steps = task.Steps.OrderBy(s => s.StepOrder).ToList();
        if (steps.Count == 0 || steps.All(s => s.Status == "Completed"))
        {
            await OrchestrateTaskAsync(userId, taskId, team.Id, ct);
            task = await _db.WorkTasks.Include(t => t.Steps.OrderBy(s => s.StepOrder)).Include(t => t.Artifacts).FirstAsync(t => t.Id == taskId && t.UserId == userId, ct);
            steps = task.Steps.OrderBy(s => s.StepOrder).ToList();
        }

        var stepTimeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds ?? 180, 30, 3600));
        var iterations = Math.Clamp(maxIterations ?? 6, 1, 50);
        var completedOutputs = (await LoadCompletedOutputsAsync(userId, task.Id, ct)).ToList();
        task.Status = "Running";
        task.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await EmitAsync("task_started", task, team, null, "Running", "Expert team execution started.", onEvent, ct);

        try
        {
            foreach (var group in BuildExecutionGroups(steps.Where(s => s.Status != "Completed").ToList()))
            {
                ct.ThrowIfCancellationRequested();
                var groupContext = completedOutputs.ToList();
                foreach (var step in group)
                {
                    step.Status = "Running";
                    step.UpdatedAt = DateTime.UtcNow;
                    await EmitAsync("step_started", task, team, step, "Running", "Expert step started.", onEvent, ct);
                }
                await _db.SaveChangesAsync(ct);

                var results = await Task.WhenAll(group.Select(step => RunPreparedStepAsync(userId, task, step, groupContext, iterations, stepTimeout, ct)));
                foreach (var result in results.OrderBy(r => r.Step.StepOrder))
                {
                    var step = result.Step;
                    if (!result.Success || result.Result is null)
                    {
                        await MarkExecutionFailureAsync(task, step, result.ErrorMessage ?? "Expert step failed.", ct);
                        await EmitAsync("step_failed", task, team, step, "Failed", result.ErrorMessage ?? "Expert step failed.", onEvent, ct);
                        await EmitAsync("task_failed", task, team, null, "Failed", result.ErrorMessage ?? "Expert team execution failed.", onEvent, ct);
                        return (await _workTasks.GetAsync(userId, taskId, ct))!;
                    }

                    var consumed = await ConsumeStepCreditsAsync(userId, task, team, step, result.Result, ct);
                    var output = AppendBillingSummary(result.Result.Output, result.Result, consumed);
                    step.Status = "Completed";
                    step.Summary = Snippet(result.Result.Output, 500);
                    step.UpdatedAt = DateTime.UtcNow;
                    AddArtifact(task, userId, $"{step.StepOrder:00}-{SafeArtifactName(step.Name)}-output.md", output);
                    completedOutputs.Add(output);
                    await EmitAsync("step_completed", task, team, step, "Completed", "Expert step completed.", onEvent, ct);
                }
                await _db.SaveChangesAsync(ct);
            }

            task.Status = "Completed";
            task.UpdatedAt = DateTime.UtcNow;
            AddArtifact(task, userId, "expert-team-execution-report.md", BuildExecutionReport(task, team, completedOutputs));
            await _db.SaveChangesAsync(ct);
            await EmitAsync("task_completed", task, team, null, "Completed", "Expert team execution completed.", onEvent, ct);
            return (await _workTasks.GetAsync(userId, taskId, ct))!;
        }
        catch (OperationCanceledException)
        {
            task.Status = "Canceled";
            task.UpdatedAt = DateTime.UtcNow;
            AddArtifact(task, userId, "expert-team-canceled.md", "# Expert team execution canceled\n");
            await _db.SaveChangesAsync(CancellationToken.None);
            await EmitAsync("task_failed", task, team, null, "Canceled", "Expert team execution was canceled.", onEvent, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            task.Status = "Failed";
            task.UpdatedAt = DateTime.UtcNow;
            AddArtifact(task, userId, "expert-team-error.md", $"# Expert team execution failed\n\n{ex.Message}\n");
            await _db.SaveChangesAsync(ct);
            await EmitAsync("task_failed", task, team, null, "Failed", ex.Message, onEvent, ct);
            throw;
        }
    }

    private async Task<ExpertTeam> ResolveTeamAsync(Guid userId, Guid? teamId, string? taskTeamId, CancellationToken ct)
    {
        await EnsureDefaultTeamsAsync(userId, ct);
        Guid? resolvedId = teamId;
        if (resolvedId is null && Guid.TryParse(taskTeamId, out var parsed)) resolvedId = parsed;
        IQueryable<ExpertTeam> query = _db.ExpertTeams.Include(t => t.Members.OrderBy(m => m.MemberOrder)).AsSplitQuery().Where(t => t.UserId == userId && t.Enabled);
        if (resolvedId is not null)
        {
            return await query.FirstOrDefaultAsync(t => t.Id == resolvedId.Value, ct)
                ?? throw new KeyNotFoundException("Expert team not found.");
        }
        return await query.OrderBy(t => t.CreatedAt).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No expert team is available.");
    }

    private async Task EnsureDefaultTeamsAsync(Guid userId, CancellationToken ct)
    {
        if (await _db.ExpertTeams.AnyAsync(t => t.UserId == userId, ct)) return;
        var fallbackAgentId = GetFallbackAgentId();
        var now = DateTime.UtcNow;
        var teams = new[]
        {
            new ExpertTeam
            {
                UserId = userId,
                Name = "QIANYUAN Strategy Expert Team",
                Description = "A cross-functional team for strategy, research, delivery, and quality review.",
                Scenario = "strategy",
                CreatedAt = now,
                UpdatedAt = now,
                Members =
                {
                    new ExpertTeamMember { UserId = userId, MemberOrder = 1, RoleId = "lead", DisplayName = "Strategy Lead", AgentId = fallbackAgentId, Responsibility = "Clarify goals, split work, and consolidate final output.", ExecutionMode = "Sequential" },
                    new ExpertTeamMember { UserId = userId, MemberOrder = 2, RoleId = "research", DisplayName = "Research Expert", AgentId = fallbackAgentId, Responsibility = "Collect context, identify constraints, and provide evidence-backed findings.", ExecutionMode = "Parallel" },
                    new ExpertTeamMember { UserId = userId, MemberOrder = 3, RoleId = "delivery", DisplayName = "Delivery Expert", AgentId = fallbackAgentId, Responsibility = "Design executable steps, milestones, and deliverables.", ExecutionMode = "Parallel" },
                    new ExpertTeamMember { UserId = userId, MemberOrder = 4, RoleId = "quality", DisplayName = "Quality Reviewer", AgentId = fallbackAgentId, Responsibility = "Review risks, assumptions, and final quality gates.", ExecutionMode = "Sequential" },
                }
            },
            new ExpertTeam
            {
                UserId = userId,
                Name = "QIANYUAN Product Expert Team",
                Description = "A team for product analysis, solution design, implementation planning, and review.",
                Scenario = "product",
                CreatedAt = now,
                UpdatedAt = now,
                Members =
                {
                    new ExpertTeamMember { UserId = userId, MemberOrder = 1, RoleId = "product", DisplayName = "Product Expert", AgentId = fallbackAgentId, Responsibility = "Define user value, scope, and acceptance criteria.", ExecutionMode = "Sequential" },
                    new ExpertTeamMember { UserId = userId, MemberOrder = 2, RoleId = "solution", DisplayName = "Solution Expert", AgentId = fallbackAgentId, Responsibility = "Create solution architecture and identify dependencies.", ExecutionMode = "Parallel" },
                    new ExpertTeamMember { UserId = userId, MemberOrder = 3, RoleId = "operations", DisplayName = "Operations Expert", AgentId = fallbackAgentId, Responsibility = "Plan rollout, monitoring, and operational safeguards.", ExecutionMode = "Parallel" },
                }
            }
        };
        _db.ExpertTeams.AddRange(teams);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<StepRunResult> RunPreparedStepAsync(Guid userId, WorkTask task, WorkStep step, IReadOnlyList<string> completedOutputs, int maxIterations, TimeSpan stepTimeout, CancellationToken ct)
    {
        var agentId = string.IsNullOrWhiteSpace(step.AgentId) ? GetFallbackAgentId() : step.AgentId!;
        var agent = _agents.Get(agentId) ?? _agents.List().FirstOrDefault();
        if (agent is null) return StepRunResult.Failed(step, "No available agent for this expert step.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(stepTimeout);
        try
        {
            var result = await RunAgentAsync(agent, userId, task, step, completedOutputs, maxIterations, timeout.Token);
            return StepRunResult.Completed(step, result);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return StepRunResult.Failed(step, $"Expert step exceeded {stepTimeout.TotalSeconds:0} seconds and was stopped.");
        }
        catch (Exception ex)
        {
            return StepRunResult.Failed(step, ex.Message);
        }
    }

    private async Task<MemberAgentResult> RunAgentAsync(IAgent agent, Guid userId, WorkTask task, WorkStep step, IReadOnlyList<string> completedOutputs, int maxIterations, CancellationToken ct)
    {
        var prompt = BuildMemberPrompt(task, step, completedOutputs);
        var run = new AgentRunRequest
        {
            SessionId = $"work-task:{task.Id:N}:step:{step.Id:N}",
            ProviderOverride = task.ProviderId,
            ModelOverride = task.Model,
            SystemPromptOverride = BuildMemberSystemPrompt(step),
            MaxIterations = maxIterations,
            Metadata = new Dictionary<string, string>
            {
                ["userId"] = userId.ToString(),
                ["taskId"] = task.Id.ToString(),
                ["stepId"] = step.Id.ToString(),
                ["expertRole"] = step.Name,
            },
            Messages = [ChatMessage.User(prompt)],
        };
        var output = new StringBuilder();
        TokenUsage? usage = null;
        await foreach (var chunk in agent.RunAsync(run, ct).ConfigureAwait(false))
        {
            if (chunk.Kind == StreamingChunkKind.TextDelta && !string.IsNullOrEmpty(chunk.Text)) output.Append(chunk.Text);
            if (chunk.Kind == StreamingChunkKind.Usage && chunk.Usage is not null) usage = chunk.Usage;
            if (chunk.Kind == StreamingChunkKind.Error) throw new InvalidOperationException(chunk.Text ?? "Agent returned an error.");
        }
        var text = output.ToString().Trim();
        if (string.IsNullOrWhiteSpace(text)) text = "The expert completed the step but returned no textual output.";
        return new MemberAgentResult(text, usage, EstimateTokens(prompt), EstimateTokens(text));
    }

    private static string BuildMemberSystemPrompt(WorkStep step)
    {
        return $"You are {step.Name}, a QIANYUAN expert team member. Focus only on your assigned responsibility: {step.Summary}. Return a structured Markdown deliverable with conclusions, rationale, risks, assumptions, and next actions.";
    }

    private static string BuildMemberPrompt(WorkTask task, WorkStep step, IReadOnlyList<string> completedOutputs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Task title: {task.Title}");
        sb.AppendLine();
        sb.AppendLine("Overall goal:");
        sb.AppendLine(task.Goal);
        sb.AppendLine();
        sb.AppendLine("Current expert responsibility:");
        sb.AppendLine(step.Summary ?? "Complete this expert step.");
        if (completedOutputs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Previous expert outputs:");
            foreach (var item in completedOutputs) sb.AppendLine(item).AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("Return only your structured Markdown deliverable. Do not explain internal orchestration.");
        return sb.ToString();
    }

    private static IReadOnlyList<IReadOnlyList<WorkStep>> BuildExecutionGroups(IReadOnlyList<WorkStep> steps)
    {
        var groups = new List<IReadOnlyList<WorkStep>>();
        var parallel = new List<WorkStep>();
        foreach (var step in steps.OrderBy(s => s.StepOrder))
        {
            if (string.Equals(step.ExecutionMode, "Parallel", StringComparison.OrdinalIgnoreCase))
            {
                parallel.Add(step);
                continue;
            }
            if (parallel.Count > 0)
            {
                groups.Add(parallel.ToList());
                parallel.Clear();
            }
            groups.Add([step]);
        }
        if (parallel.Count > 0) groups.Add(parallel.ToList());
        return groups;
    }

    private async Task<IReadOnlyList<string>> LoadCompletedOutputsAsync(Guid userId, Guid taskId, CancellationToken ct)
    {
        return await _db.WorkArtifacts.AsNoTracking()
            .Where(a => a.UserId == userId && a.TaskId == taskId && a.Name.EndsWith("-output.md"))
            .OrderBy(a => a.CreatedAt)
            .Select(a => a.Content ?? string.Empty)
            .ToListAsync(ct);
    }

    private async Task MarkExecutionFailureAsync(WorkTask task, WorkStep step, string message, CancellationToken ct)
    {
        var normalized = string.IsNullOrWhiteSpace(message) ? "Execution failed without details." : message.Trim();
        step.Status = "Failed";
        step.Summary = Snippet(normalized, 500);
        step.UpdatedAt = DateTime.UtcNow;
        task.Status = "Failed";
        task.UpdatedAt = DateTime.UtcNow;
        AddArtifact(task, task.UserId, $"{step.StepOrder:00}-{SafeArtifactName(step.Name)}-failed.md", $"# {step.Name} failed\n\n{normalized}\n");
        await _db.SaveChangesAsync(ct);
    }

    private async Task<long> ConsumeStepCreditsAsync(Guid userId, WorkTask task, ExpertTeam team, WorkStep step, MemberAgentResult result, CancellationToken ct)
    {
        var inputTokens = result.Usage?.InputTokens ?? result.EstimatedInputTokens;
        var outputTokens = result.Usage?.OutputTokens ?? result.EstimatedOutputTokens;
        var estimate = _credits.Estimate(new EstimateCreditsRequest(inputTokens, outputTokens, ResolveModelTier(task.ProviderId, task.Model), "expert-team"));
        var amount = Math.Max(1, estimate.EstimatedCredits);
        await _credits.ConsumeAsync(userId, new ConsumeCreditsRequest(amount, "WorkTask", task.Id.ToString(), $"Expert team execution: {team.Name} / {step.Name}"), ct);
        return amount;
    }

    private static string BuildPlanArtifact(WorkTask task, ExpertTeam team, IReadOnlyList<ExpertTeamMember> members)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {task.Title} - Expert Team Plan");
        sb.AppendLine();
        sb.AppendLine($"Team: {team.Name}");
        sb.AppendLine($"Scenario: {team.Scenario}");
        sb.AppendLine();
        sb.AppendLine("## Goal");
        sb.AppendLine(task.Goal);
        sb.AppendLine();
        sb.AppendLine("## Members");
        foreach (var member in members.OrderBy(m => m.MemberOrder))
        {
            sb.AppendLine($"- {member.MemberOrder}. {member.DisplayName} ({member.ExecutionMode}): {member.Responsibility}");
        }
        return sb.ToString();
    }

    private static string BuildExecutionReport(WorkTask task, ExpertTeam team, IReadOnlyList<string> completedOutputs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {task.Title} - Expert Team Execution Report");
        sb.AppendLine();
        sb.AppendLine($"Team: {team.Name}");
        sb.AppendLine($"Completed at (UTC): {DateTime.UtcNow:O}");
        sb.AppendLine();
        sb.AppendLine("## Outputs");
        sb.AppendLine();
        if (completedOutputs.Count == 0)
        {
            sb.AppendLine("No expert output was generated.");
        }
        else
        {
            foreach (var item in completedOutputs) sb.AppendLine(item).AppendLine();
        }
        return sb.ToString();
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

    private static async Task EmitAsync(string type, WorkTask task, ExpertTeam team, WorkStep? step, string status, string? message, Func<ExpertTeamExecutionEventDto, CancellationToken, Task>? onEvent, CancellationToken ct)
    {
        if (onEvent is null) return;
        var dto = new ExpertTeamExecutionEventDto(type, task.Id, team.Id, step?.Id, step?.StepOrder, step?.Name, step?.ExecutionMode, status, message, DateTime.UtcNow);
        await onEvent(dto, ct);
    }

    private string GetFallbackAgentId() => _agents.List().FirstOrDefault()?.Id ?? "qianyuan.default";

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

    private static string NormalizeExecutionMode(string? value) =>
        string.Equals(value, "Parallel", StringComparison.OrdinalIgnoreCase) ? "Parallel" : "Sequential";

    private static string SafeArtifactName(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray();
        var normalized = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "expert-step" : normalized;
    }

    private static ExpertTeamDto ToDto(ExpertTeam team) =>
        new(team.Id, team.Name, team.Description, team.Scenario, team.Enabled, team.CreatedAt, team.UpdatedAt, team.Members.OrderBy(m => m.MemberOrder).Select(ToDto).ToList());

    private static ExpertTeamMemberDto ToDto(ExpertTeamMember member) =>
        new(member.Id, member.MemberOrder, member.RoleId, member.DisplayName, member.AgentId, member.Responsibility, member.ExecutionMode, member.Enabled);

    private sealed record MemberAgentResult(string Output, TokenUsage? Usage, int EstimatedInputTokens, int EstimatedOutputTokens);

    private sealed record StepRunResult(WorkStep Step, bool Success, MemberAgentResult? Result, string? ErrorMessage)
    {
        public static StepRunResult Completed(WorkStep step, MemberAgentResult result) => new(step, true, result, null);
        public static StepRunResult Failed(WorkStep step, string message) => new(step, false, null, message);
    }
}
