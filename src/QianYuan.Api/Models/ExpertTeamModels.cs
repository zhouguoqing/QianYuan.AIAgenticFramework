namespace QianYuan.Api.Models;

public sealed record CreateExpertTeamRequest(
    string Name,
    string? Description,
    string? Scenario,
    IReadOnlyList<CreateExpertTeamMemberRequest>? Members);

public sealed record CreateExpertTeamMemberRequest(
    string RoleId,
    string DisplayName,
    string? AgentId,
    string Responsibility,
    string? ExecutionMode);

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

public sealed record OrchestrateTaskRequest(Guid? TeamId);

public sealed record ExecuteTaskRequest(Guid? TeamId, int? MaxIterations, int? TimeoutSeconds);