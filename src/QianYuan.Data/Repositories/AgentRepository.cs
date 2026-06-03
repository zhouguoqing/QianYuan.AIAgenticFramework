using Microsoft.EntityFrameworkCore;
using QianYuan.Data.Entities;

namespace QianYuan.Data.Repositories;

/// <summary>
/// Agent 仓储接口
/// </summary>
public interface IAgentRepository
{
    Task<Agent?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<Agent>> GetAllAsync(CancellationToken ct = default);
    Task<Agent> CreateAsync(Agent agent, CancellationToken ct = default);
    Task<Agent> UpdateAsync(Agent agent, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task<AgentSkill> AddSkillAsync(string agentId, string skillId, int priority = 0, CancellationToken ct = default);
    Task RemoveSkillAsync(string agentId, int skillId, CancellationToken ct = default);
    Task<AgentMcpServer> AddMcpServerAsync(AgentMcpServer server, CancellationToken ct = default);
    Task RemoveMcpServerAsync(int id, CancellationToken ct = default);
    Task<AgentCliService> AddCliServiceAsync(AgentCliService service, CancellationToken ct = default);
    Task RemoveCliServiceAsync(int id, CancellationToken ct = default);
}

/// <summary>
/// Agent 仓储实现
/// </summary>
public class AgentRepository : IAgentRepository
{
    private readonly QianYuanDbContext _context;

    public AgentRepository(QianYuanDbContext context)
    {
        _context = context;
    }

    public async Task<Agent?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        return await _context.Agents
            .Include(a => a.Skills)
            .Include(a => a.McpServers)
            .Include(a => a.CliServices)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<List<Agent>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Agents
            .Include(a => a.Skills)
            .Include(a => a.McpServers)
            .Include(a => a.CliServices)
            .AsSplitQuery()
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<Agent> CreateAsync(Agent agent, CancellationToken ct = default)
    {
        _context.Agents.Add(agent);
        await _context.SaveChangesAsync(ct);
        return agent;
    }

    public async Task<Agent> UpdateAsync(Agent agent, CancellationToken ct = default)
    {
        agent.UpdatedAt = DateTime.UtcNow;
        _context.Agents.Update(agent);
        await _context.SaveChangesAsync(ct);
        return agent;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var agent = await _context.Agents.FindAsync(new object[] { id }, cancellationToken: ct);
        if (agent != null)
        {
            _context.Agents.Remove(agent);
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task<AgentSkill> AddSkillAsync(string agentId, string skillId, int priority = 0, CancellationToken ct = default)
    {
        var skill = new AgentSkill
        {
            AgentId = agentId,
            SkillId = skillId,
            Priority = priority,
            CreatedAt = DateTime.UtcNow
        };
        _context.AgentSkills.Add(skill);
        await _context.SaveChangesAsync(ct);
        return skill;
    }

    public async Task RemoveSkillAsync(string agentId, int skillId, CancellationToken ct = default)
    {
        var skill = await _context.AgentSkills.FindAsync(new object[] { skillId }, cancellationToken: ct);
        if (skill != null)
        {
            _context.AgentSkills.Remove(skill);
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task<AgentMcpServer> AddMcpServerAsync(AgentMcpServer server, CancellationToken ct = default)
    {
        _context.AgentMcpServers.Add(server);
        await _context.SaveChangesAsync(ct);
        return server;
    }

    public async Task RemoveMcpServerAsync(int id, CancellationToken ct = default)
    {
        var server = await _context.AgentMcpServers.FindAsync(new object[] { id }, cancellationToken: ct);
        if (server != null)
        {
            _context.AgentMcpServers.Remove(server);
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task<AgentCliService> AddCliServiceAsync(AgentCliService service, CancellationToken ct = default)
    {
        _context.AgentCliServices.Add(service);
        await _context.SaveChangesAsync(ct);
        return service;
    }

    public async Task RemoveCliServiceAsync(int id, CancellationToken ct = default)
    {
        var service = await _context.AgentCliServices.FindAsync(new object[] { id }, cancellationToken: ct);
        if (service != null)
        {
            _context.AgentCliServices.Remove(service);
            await _context.SaveChangesAsync(ct);
        }
    }
}
