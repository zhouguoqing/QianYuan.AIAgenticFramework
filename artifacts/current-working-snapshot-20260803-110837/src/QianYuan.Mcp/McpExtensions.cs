using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QianYuan.Core.Abstractions;
using QianYuan.Mcp.Client;
using QianYuan.Mcp.Server;

namespace QianYuan.Mcp;

public static class McpExtensions
{
    /// <summary>
    /// Register an external MCP server (stdio transport) and mount its tools as a progressively-loaded skill.
    /// The connection is established lazily the first time the skill's tools are requested.
    /// </summary>
    public static IServiceCollection AddMcpStdioServer(this IServiceCollection services, McpStdioServerConfig config)
    {
        services.AddSingleton<IMcpClient>(sp =>
            new StdioMcpClient(config, sp.GetRequiredService<ILoggerFactory>().CreateLogger($"mcp.{config.ServerId}")));
        return services;
    }

    /// <summary>
    /// After building the service provider, mount every registered IMcpClient as an McpSkill into the SkillManager.
    /// </summary>
    public static void MountMcpSkills(this IServiceProvider sp)
    {
        var skills = sp.GetRequiredService<ISkillManager>();
        foreach (var client in sp.GetServices<IMcpClient>())
        {
            var skill = new McpSkill(client);
            skills.Register(skill);
        }
    }

    /// <summary>Build a server core that exposes the kernel's skills as MCP tools.</summary>
    public static McpServerCore CreateMcpServerCore(this IServiceProvider sp, string serverName = "qianyuan")
        => new(
            sp.GetRequiredService<ISkillManager>(),
            sp,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<McpServerCore>(),
            serverName);
}
