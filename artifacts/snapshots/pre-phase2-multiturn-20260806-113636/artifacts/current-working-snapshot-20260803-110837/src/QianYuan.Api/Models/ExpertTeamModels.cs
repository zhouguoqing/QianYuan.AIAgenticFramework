namespace QianYuan.Api.Models;

public sealed record CreateExpertTeamRequest(
    string Name,
    string? Description,
    string? Scenario,
    IReadOnlyList<CreateExpertTeamMemberRequest>? Members);

public sealed record UpdateExpertTeamRequest(
    string Name,
    string? Description,
    string? Scenario,
    bool? Enabled);

public sealed record CreateExpertTeamMemberRequest(
    string RoleId,
    string DisplayName,
    string? AgentId,
    string Responsibility,
    string? ExecutionMode);

public sealed record UpdateExpertTeamMemberRequest(
    int? MemberOrder,
    string RoleId,
    string DisplayName,
    string? AgentId,
    string Responsibility,
    string? ExecutionMode,
    bool? Enabled);

public sealed record ExpertTeamMemberDto(
    Guid Id,
    int MemberOrder,
    string RoleId,
    string DisplayName,
    string AgentId,
    string Responsibility,
    string ExecutionMode,
    bool Enabled);

public sealed record ExpertTeamDto(
    Guid Id,
    string Name,
    string Description,
    string Scenario,
    bool Enabled,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<ExpertTeamMemberDto> Members);

public sealed record ExpertTeamTemplateMemberDto(
    string RoleId,
    string DisplayName,
    string Profession,
    string Responsibility,
    string ExecutionMode);

public sealed record ExpertTeamTemplateDto(
    string Id,
    string Name,
    string Description,
    string Scenario,
    string CategoryId,
    IReadOnlyList<string> Tags,
    string DefaultInitPrompt,
    IReadOnlyList<ExpertTeamTemplateMemberDto> Members);

public sealed record OrchestrateTaskRequest(Guid? TeamId);

public sealed record ExecuteTaskRequest(Guid? TeamId, int? MaxIterations, int? TimeoutSeconds);

public sealed record ExpertTeamExecutionEventDto(
    string Type,
    Guid TaskId,
    Guid? TeamId,
    Guid? StepId,
    int? StepOrder,
    string? StepName,
    string? ExecutionMode,
    string Status,
    string? Message,
    DateTime At);
