namespace QianYuan.Api.Models;

public sealed record CreateWorkTaskRequest(
    string Title,
    string Goal,
    string? TeamId,
    string? ProviderId,
    string? Model);

public sealed record WorkTaskDto(
    Guid Id,
    string Title,
    string Goal,
    string Status,
    string? TeamId,
    string? ProviderId,
    string? Model,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int StepCount,
    int ArtifactCount);

public sealed record WorkStepDto(
    Guid Id,
    int StepOrder,
    string Name,
    string Status,
    string? AgentId,
    string? Summary,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record WorkArtifactDto(
    Guid Id,
    Guid TaskId,
    string Name,
    string ContentType,
    string StorageKind,
    string? Content,
    string? FilePath,
    long SizeBytes,
    DateTime CreatedAt);

public sealed record WorkTaskDetailDto(
    WorkTaskDto Task,
    IReadOnlyList<WorkStepDto> Steps,
    IReadOnlyList<WorkArtifactDto> Artifacts);