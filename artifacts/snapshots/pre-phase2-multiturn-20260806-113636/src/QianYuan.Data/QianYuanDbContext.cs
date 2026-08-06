using Microsoft.EntityFrameworkCore;
using QianYuan.Data.Entities;

namespace QianYuan.Data;

/// <summary>
/// QianYuan 数据库上下文
/// </summary>
public class QianYuanDbContext : DbContext
{
    public QianYuanDbContext(DbContextOptions<QianYuanDbContext> options) : base(options)
    {
    }

    public DbSet<Agent> Agents { get; set; }
    public DbSet<AgentSkill> AgentSkills { get; set; }
    public DbSet<AgentMcpServer> AgentMcpServers { get; set; }
    public DbSet<AgentCliService> AgentCliServices { get; set; }
    public DbSet<AgentTestSession> AgentTestSessions { get; set; }
    public DbSet<UserAccount> UserAccounts { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<CreditWallet> CreditWallets { get; set; }
    public DbSet<CreditTransaction> CreditTransactions { get; set; }
    public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }
    public DbSet<UserSubscription> UserSubscriptions { get; set; }
    public DbSet<WorkTask> WorkTasks { get; set; }
    public DbSet<WorkStep> WorkSteps { get; set; }
    public DbSet<WorkArtifact> WorkArtifacts { get; set; }
    public DbSet<ExpertTeam> ExpertTeams { get; set; }
    public DbSet<ExpertTeamMember> ExpertTeamMembers { get; set; }
    public DbSet<CustomExpert> CustomExperts { get; set; }
    public DbSet<SkillPackage> SkillPackages { get; set; }
    public DbSet<SkillMarketEntry> SkillMarketEntries { get; set; }
    public DbSet<InstalledSkill> InstalledSkills { get; set; }
    public DbSet<Conversation> Conversations { get; set; }
    public DbSet<ConversationMessage> ConversationMessages { get; set; }
    public DbSet<ConversationTurn> ConversationTurns { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Agent configuration
        modelBuilder.Entity<Agent>()
            .HasKey(a => a.Id);

        modelBuilder.Entity<Conversation>()
            .HasKey(c => c.Id);

        modelBuilder.Entity<Conversation>()
            .HasIndex(c => new { c.UserId, c.UpdatedAt });

        modelBuilder.Entity<Conversation>()
            .Property(c => c.Id)
            .HasMaxLength(80);

        modelBuilder.Entity<Conversation>()
            .Property(c => c.UserId)
            .HasMaxLength(80);

        modelBuilder.Entity<Conversation>()
            .Property(c => c.Title)
            .HasMaxLength(240);

        modelBuilder.Entity<Conversation>()
            .Property(c => c.AgentId)
            .HasMaxLength(256);

        modelBuilder.Entity<Conversation>()
            .Property(c => c.Status)
            .HasMaxLength(40);

        modelBuilder.Entity<ConversationMessage>()
            .HasKey(m => m.Id);

        modelBuilder.Entity<ConversationMessage>()
            .HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ConversationMessage>()
            .HasIndex(m => new { m.ConversationId, m.SortOrder });

        modelBuilder.Entity<ConversationMessage>()
            .Property(m => m.Role)
            .HasMaxLength(40);

        modelBuilder.Entity<ConversationTurn>()
            .HasKey(t => t.Id);

        modelBuilder.Entity<ConversationTurn>()
            .HasOne(t => t.Conversation)
            .WithMany(c => c.Turns)
            .HasForeignKey(t => t.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ConversationTurn>()
            .HasIndex(t => new { t.ConversationId, t.CreatedAt });

        modelBuilder.Entity<SkillPackage>()
            .HasKey(p => p.Id);

        modelBuilder.Entity<SkillPackage>()
            .Property(p => p.Name)
            .HasMaxLength(160);

        modelBuilder.Entity<SkillPackage>()
            .Property(p => p.Category)
            .HasMaxLength(80);

        modelBuilder.Entity<SkillMarketEntry>()
            .HasKey(e => e.Id);

        modelBuilder.Entity<SkillMarketEntry>()
            .HasOne(e => e.Package)
            .WithMany(p => p.Entries)
            .HasForeignKey(e => e.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SkillMarketEntry>()
            .HasIndex(e => new { e.PackageId, e.SortOrder });

        modelBuilder.Entity<SkillMarketEntry>()
            .Property(e => e.Name)
            .HasMaxLength(160);

        modelBuilder.Entity<SkillMarketEntry>()
            .Property(e => e.Category)
            .HasMaxLength(80);

        modelBuilder.Entity<InstalledSkill>()
            .HasKey(s => s.SkillId);

        modelBuilder.Entity<InstalledSkill>()
            .HasIndex(s => s.MarketEntryId);

        modelBuilder.Entity<InstalledSkill>()
            .Property(s => s.Name)
            .HasMaxLength(160);

        modelBuilder.Entity<InstalledSkill>()
            .Property(s => s.Category)
            .HasMaxLength(80);

        modelBuilder.Entity<InstalledSkill>()
            .Property(s => s.Scope)
            .HasMaxLength(40);


        modelBuilder.Entity<UserAccount>()
            .HasKey(u => u.Id);

        modelBuilder.Entity<UserAccount>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<UserAccount>()
            .Property(u => u.Email)
            .HasMaxLength(320);

        modelBuilder.Entity<UserAccount>()
            .Property(u => u.DisplayName)
            .HasMaxLength(120);

        modelBuilder.Entity<UserAccount>()
            .Property(u => u.Status)
            .HasMaxLength(40);

        modelBuilder.Entity<RefreshToken>()
            .HasKey(t => t.Id);

        modelBuilder.Entity<RefreshToken>()
            .HasOne(t => t.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(t => t.TokenHash)
            .IsUnique();

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(t => t.UserId);

        modelBuilder.Entity<CreditWallet>()
            .HasKey(w => w.Id);

        modelBuilder.Entity<CreditWallet>()
            .HasOne(w => w.User)
            .WithMany()
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CreditWallet>()
            .HasIndex(w => w.UserId)
            .IsUnique();

        modelBuilder.Entity<CreditWallet>()
            .Property(w => w.QuotaMonth)
            .HasMaxLength(7);

        modelBuilder.Entity<CreditTransaction>()
            .HasKey(t => t.Id);

        modelBuilder.Entity<CreditTransaction>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CreditTransaction>()
            .HasIndex(t => new { t.UserId, t.CreatedAt });

        modelBuilder.Entity<CreditTransaction>()
            .Property(t => t.Type)
            .HasMaxLength(40);

        modelBuilder.Entity<CreditTransaction>()
            .Property(t => t.SourceType)
            .HasMaxLength(80);

        modelBuilder.Entity<CreditTransaction>()
            .Property(t => t.SourceId)
            .HasMaxLength(120);

        modelBuilder.Entity<SubscriptionPlan>()
            .HasKey(p => p.Id);

        modelBuilder.Entity<SubscriptionPlan>()
            .Property(p => p.Id)
            .HasMaxLength(40);

        modelBuilder.Entity<SubscriptionPlan>()
            .Property(p => p.Name)
            .HasMaxLength(80);

        modelBuilder.Entity<UserSubscription>()
            .HasKey(s => s.Id);

        modelBuilder.Entity<UserSubscription>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserSubscription>()
            .HasOne(s => s.Plan)
            .WithMany()
            .HasForeignKey(s => s.PlanId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<UserSubscription>()
            .HasIndex(s => new { s.UserId, s.Status });

        modelBuilder.Entity<UserSubscription>()
            .Property(s => s.Status)
            .HasMaxLength(40);

        modelBuilder.Entity<WorkTask>()
            .HasKey(t => t.Id);

        modelBuilder.Entity<WorkTask>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorkTask>()
            .HasIndex(t => new { t.UserId, t.UpdatedAt });

        modelBuilder.Entity<WorkTask>()
            .Property(t => t.Title)
            .HasMaxLength(240);

        modelBuilder.Entity<WorkTask>()
            .Property(t => t.Status)
            .HasMaxLength(40);

        modelBuilder.Entity<WorkStep>()
            .HasKey(s => s.Id);

        modelBuilder.Entity<WorkStep>()
            .HasOne(s => s.Task)
            .WithMany(t => t.Steps)
            .HasForeignKey(s => s.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorkStep>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorkStep>()
            .HasIndex(s => new { s.TaskId, s.StepOrder });

        modelBuilder.Entity<WorkStep>()
            .Property(s => s.Name)
            .HasMaxLength(160);

        modelBuilder.Entity<WorkStep>()
            .Property(s => s.Status)
            .HasMaxLength(40);

        modelBuilder.Entity<WorkStep>()
            .Property(s => s.ExecutionMode)
            .HasMaxLength(40);

        modelBuilder.Entity<WorkArtifact>()
            .HasKey(a => a.Id);

        modelBuilder.Entity<WorkArtifact>()
            .HasOne(a => a.Task)
            .WithMany(t => t.Artifacts)
            .HasForeignKey(a => a.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorkArtifact>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorkArtifact>()
            .HasIndex(a => new { a.TaskId, a.CreatedAt });

        modelBuilder.Entity<WorkArtifact>()
            .Property(a => a.Name)
            .HasMaxLength(240);

        modelBuilder.Entity<WorkArtifact>()
            .Property(a => a.ContentType)
            .HasMaxLength(120);

        modelBuilder.Entity<WorkArtifact>()
            .Property(a => a.StorageKind)
            .HasMaxLength(40);

        modelBuilder.Entity<ExpertTeam>()
            .HasKey(t => t.Id);

        modelBuilder.Entity<ExpertTeam>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExpertTeam>()
            .HasIndex(t => new { t.UserId, t.Name });

        modelBuilder.Entity<ExpertTeam>()
            .Property(t => t.Name)
            .HasMaxLength(120);

        modelBuilder.Entity<ExpertTeam>()
            .Property(t => t.Scenario)
            .HasMaxLength(80);

        modelBuilder.Entity<ExpertTeamMember>()
            .HasKey(m => m.Id);

        modelBuilder.Entity<ExpertTeamMember>()
            .HasOne(m => m.Team)
            .WithMany(t => t.Members)
            .HasForeignKey(m => m.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExpertTeamMember>()
            .HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExpertTeamMember>()
            .HasIndex(m => new { m.TeamId, m.MemberOrder });

        modelBuilder.Entity<ExpertTeamMember>()
            .Property(m => m.RoleId)
            .HasMaxLength(80);

        modelBuilder.Entity<ExpertTeamMember>()
            .Property(m => m.DisplayName)
            .HasMaxLength(120);

        modelBuilder.Entity<ExpertTeamMember>()
            .Property(m => m.AgentId)
            .HasMaxLength(256);

        modelBuilder.Entity<ExpertTeamMember>()
            .Property(m => m.ExecutionMode)
            .HasMaxLength(40);

        modelBuilder.Entity<CustomExpert>()
            .HasKey(e => e.Id);

        modelBuilder.Entity<CustomExpert>()
            .HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CustomExpert>()
            .HasIndex(e => new { e.UserId, e.Name });

        modelBuilder.Entity<CustomExpert>()
            .Property(e => e.Id)
            .HasMaxLength(256);

        modelBuilder.Entity<CustomExpert>()
            .Property(e => e.CategoryId)
            .HasMaxLength(80);

        modelBuilder.Entity<CustomExpert>()
            .Property(e => e.Name)
            .HasMaxLength(120);

        modelBuilder.Entity<CustomExpert>()
            .Property(e => e.Profession)
            .HasMaxLength(160);

        modelBuilder.Entity<CustomExpert>()
            .Property(e => e.AvatarUrl)
            .HasMaxLength(1000);

        modelBuilder.Entity<CustomExpert>()
            .Property(e => e.BoundAgentId)
            .HasMaxLength(256);
        
        modelBuilder.Entity<Agent>()
            .Property(a => a.Id)
            .HasMaxLength(256);

        // AgentSkill configuration
        modelBuilder.Entity<AgentSkill>()
            .HasOne(s => s.Agent)
            .WithMany(a => a.Skills)
            .HasForeignKey(s => s.AgentId)
            .OnDelete(DeleteBehavior.Cascade);

        // AgentMcpServer configuration
        modelBuilder.Entity<AgentMcpServer>()
            .HasOne(m => m.Agent)
            .WithMany(a => a.McpServers)
            .HasForeignKey(m => m.AgentId)
            .OnDelete(DeleteBehavior.Cascade);

        // AgentCliService configuration
        modelBuilder.Entity<AgentCliService>()
            .HasOne(c => c.Agent)
            .WithMany(a => a.CliServices)
            .HasForeignKey(c => c.AgentId)
            .OnDelete(DeleteBehavior.Cascade);

        // AgentTestSession configuration
        modelBuilder.Entity<AgentTestSession>()
            .HasKey(s => s.Id);

        modelBuilder.Entity<AgentTestSession>()
            .HasOne(s => s.Agent)
            .WithMany(a => a.TestSessions)
            .HasForeignKey(s => s.AgentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Create indexes for better query performance
        modelBuilder.Entity<Agent>()
            .HasIndex(a => a.CreatedAt);

        modelBuilder.Entity<AgentSkill>()
            .HasIndex(s => s.AgentId);

        modelBuilder.Entity<AgentMcpServer>()
            .HasIndex(m => m.AgentId);

        modelBuilder.Entity<AgentCliService>()
            .HasIndex(c => c.AgentId);

        modelBuilder.Entity<AgentTestSession>()
            .HasIndex(s => s.AgentId);
    }
}
