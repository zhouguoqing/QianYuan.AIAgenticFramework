namespace QianYuan.Data.Entities;

public sealed class WorkTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public string? TeamId { get; set; }
    public string? ProviderId { get; set; }
    public string? Model { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public UserAccount User { get; set; } = null!;
    public ICollection<WorkStep> Steps { get; set; } = [];
    public ICollection<WorkArtifact> Artifacts { get; set; } = [];
}

public sealed class WorkStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid UserId { get; set; }
    public int StepOrder { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? AgentId { get; set; }
    public string? Summary { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public WorkTask Task { get; set; } = null!;
    public UserAccount User { get; set; } = null!;
}

public sealed class WorkArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ContentType { get; set; } = "text/markdown";
    public string StorageKind { get; set; } = "Database";
    public string? Content { get; set; }
    public string? FilePath { get; set; }
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public WorkTask Task { get; set; } = null!;
    public UserAccount User { get; set; } = null!;
}