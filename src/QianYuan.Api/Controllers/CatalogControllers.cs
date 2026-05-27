using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using QianYuan.Api.Configuration;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Memory;
using QianYuan.Mcp.Client;

namespace QianYuan.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AgentsController : ControllerBase
{
    private readonly IAgentRegistry _agents;
    public AgentsController(IAgentRegistry agents) => _agents = agents;

    [HttpGet]
    public IActionResult List() => Ok(_agents.List().Select(a => new
    {
        a.Id, a.Name, a.Description, a.Tags
    }));
}

[ApiController]
[Route("api/[controller]")]
public sealed class SkillsController : ControllerBase
{
    private readonly ISkillManager _skills;
    private readonly ILoggerFactory _loggerFactory;
    public SkillsController(ISkillManager skills, ILoggerFactory loggerFactory)
    {
        _skills = skills;
        _loggerFactory = loggerFactory;
    }

    [HttpGet]
    public IActionResult List() => Ok(_skills.ListManifests().Select(m => new
    {
        m.Id, m.Name, m.Description, m.Tags,
        m.ApproximateToolCount, m.RequiresNetwork, m.RequiresFilesystem,
        Enabled = _skills.IsEnabled(m.Id),
    }));

    [HttpGet("relevant")]
    public async Task<IActionResult> Relevant([FromQuery] string q, [FromQuery] int topK = 8, CancellationToken ct = default)
    {
        var picked = await _skills.SelectRelevantAsync(q, topK, ct);
        return Ok(picked);
    }

    [HttpGet("{skillId}/tools")]
    public async Task<IActionResult> Tools(string skillId, CancellationToken ct)
    {
        try
        {
            var skill = await _skills.GetAsync(skillId, ct);
            var tools = await skill.GetToolsAsync(ct);
            return Ok(new
            {
                skillId,
                systemPromptFragment = skill.SystemPromptFragment,
                enabled = _skills.IsEnabled(skillId),
                tools = tools.Select(t => new { t.Name, t.Description, t.JsonSchema, t.SkillId }),
            });
        }
        catch (Core.Exceptions.SkillNotFoundException) { return NotFound(); }
    }

    public sealed class EnabledPatch { public bool Enabled { get; set; } }

    [HttpPost("{skillId}/enabled")]
    public IActionResult SetEnabled(string skillId, [FromBody] EnabledPatch patch)
    {
        if (!_skills.ListManifests().Any(m => string.Equals(m.Id, skillId, StringComparison.OrdinalIgnoreCase)))
            return NotFound();
        _skills.SetEnabled(skillId, patch.Enabled);
        return Ok(new { skillId, enabled = patch.Enabled });
    }

    public sealed class McpStdioRegistration
    {
        public string ServerId { get; set; } = "";
        public string Command { get; set; } = "";
        public List<string> Arguments { get; set; } = new();
        public Dictionary<string, string> Environment { get; set; } = new();
    }

    [HttpPost("register/mcp-stdio")]
    public async Task<IActionResult> RegisterMcpStdio([FromBody] McpStdioRegistration body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.ServerId) || string.IsNullOrWhiteSpace(body.Command))
            return BadRequest(new { error = "ServerId and Command are required." });

        var skillId = $"mcp.{body.ServerId}";
        if (_skills.ListManifests().Any(m => string.Equals(m.Id, skillId, StringComparison.OrdinalIgnoreCase)))
            return Conflict(new { error = $"skill '{skillId}' already registered" });

        var config = new McpStdioServerConfig
        {
            ServerId = body.ServerId,
            Command = body.Command,
            Arguments = body.Arguments,
            Environment = body.Environment,
        };

        var logger = _loggerFactory.CreateLogger($"mcp.{body.ServerId}");
        var client = new StdioMcpClient(config, logger);
        try
        {
            await client.ConnectAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = $"failed to start MCP server: {ex.Message}" });
        }

        var skill = new McpSkill(client);
        _skills.Register(skill);
        return Ok(new { skillId = skill.Id, name = skill.Name });
    }
}

[ApiController]
[Route("api/[controller]")]
public sealed class ProvidersController : ControllerBase
{
    private readonly ILlmProviderRegistry _providers;
    private readonly ProviderModelCatalog _catalog;
    public ProvidersController(ILlmProviderRegistry providers, ProviderModelCatalog catalog)
    {
        _providers = providers;
        _catalog = catalog;
    }

    [HttpGet]
    public IActionResult List() => Ok(new
    {
        defaultProviderId = _catalog.DefaultProviderId,
        providers = _providers.List().Select(p =>
        {
            var models = _catalog.ModelsFor(p.ProviderId);
            return new
            {
                p.ProviderId,
                DefaultModel = _catalog.DefaultModelFor(p.ProviderId) ?? p.DefaultModel,
                Models = models.Count > 0 ? models : new[] { p.DefaultModel },
                capabilities = p.Capabilities.ToString().Split(", "),
            };
        }),
    });
}

[ApiController]
[Route("api/[controller]")]
public sealed class SessionsController : ControllerBase
{
    private readonly ISessionStore _store;
    public SessionsController(ISessionStore store) => _store = store;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? ownerId, [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _store.ListAsync(ownerId, take, ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var s = await _store.GetAsync(id, ct);
        return s is null ? NotFound() : Ok(s);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await _store.DeleteAsync(id, ct);
        return NoContent();
    }
}
