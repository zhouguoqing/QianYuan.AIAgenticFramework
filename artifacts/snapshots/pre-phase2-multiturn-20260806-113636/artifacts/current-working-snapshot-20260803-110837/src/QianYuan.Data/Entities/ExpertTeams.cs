namespace QianYuan.Data.Entities;

public sealed class ExpertTeam
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Scenario { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public UserAccount User { get; set; } = null!;
    public ICollection<ExpertTeamMember> Members { get; set; } = [];
}

public sealed class ExpertTeamMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeamId { get; set; }
    public Guid UserId { get; set; }
    public int MemberOrder { get; set; }
    public string RoleId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Responsibility { get; set; } = string.Empty;
    public string ExecutionMode { get; set; } = "Sequential";
    public bool Enabled { get; set; } = true;

    public ExpertTeam Team { get; set; } = null!;
    public UserAccount User { get; set; } = null!;
}