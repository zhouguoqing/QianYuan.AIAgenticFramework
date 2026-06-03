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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Agent configuration
        modelBuilder.Entity<Agent>()
            .HasKey(a => a.Id);
        
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
