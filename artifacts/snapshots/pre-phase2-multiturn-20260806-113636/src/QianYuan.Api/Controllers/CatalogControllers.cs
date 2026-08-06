using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using QianYuan.Api.Models;
using QianYuan.Api.Services;
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
    private readonly ISkillMarketplaceService _marketplace;
    private readonly ILoggerFactory _loggerFactory;
    public SkillsController(ISkillManager skills, ISkillMarketplaceService marketplace, ILoggerFactory loggerFactory)
    {
        _skills = skills;
        _marketplace = marketplace;
        _loggerFactory = loggerFactory;
    }

    [HttpGet]
    public IActionResult List() => Ok(_skills.ListManifests().Select(m => new
    {
        m.Id, m.Name, m.Description, m.Tags,
        m.ApproximateToolCount, m.RequiresNetwork, m.RequiresFilesystem,
        m.Category, TriggerPhrases = m.TriggerPhrases ?? Array.Empty<string>(),
        Enabled = _skills.IsEnabled(m.Id),
    }));

    [HttpGet("market")]
    public async Task<IActionResult> Market([FromQuery] string? category, [FromQuery] string? q, CancellationToken ct)
        => Ok(await _marketplace.ListMarketAsync(category, q, ct));

    [HttpGet("categories")]
    public async Task<IActionResult> Categories(CancellationToken ct)
        => Ok(await _marketplace.ListCategoriesAsync(ct));

    [HttpGet("installed")]
    public async Task<IActionResult> Installed(CancellationToken ct)
        => Ok(await _marketplace.ListInstalledAsync(ct));

    [HttpPost("install")]
    public async Task<IActionResult> Install([FromBody] InstallSkillRequest request, CancellationToken ct)
    {
        try { return Ok(await _marketplace.InstallAsync(request, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] CreateSkillRequest request, CancellationToken ct)
    {
        try { return Ok(await _marketplace.CreateAsync(request, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] string? category, CancellationToken ct)
        => Ok(await _marketplace.SearchAsync(q, category, ct));

    [HttpDelete("installed/{skillId}")]
    public async Task<IActionResult> Uninstall(string skillId, CancellationToken ct)
        => await _marketplace.UninstallAsync(skillId, ct) ? NoContent() : NotFound();

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
    [HttpPut("{skillId}/enabled")]
    public async Task<IActionResult> SetEnabled(string skillId, [FromBody] EnabledPatch patch, CancellationToken ct)
    {
        if (!await _marketplace.SetEnabledAsync(skillId, patch.Enabled, ct)) return NotFound();
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
    public async Task<IActionResult> List([FromQuery] string? ownerId, [FromQuery] string? q, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var rows = await _store.ListAsync(ownerId, take, ct);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var keyword = q.Trim();
            rows = rows.Where(s => (s.Title ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || (s.AgentId ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || s.SessionId.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        return Ok(rows);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var s = await _store.GetAsync(id, ct);
        return s is null ? NotFound() : Ok(s);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SessionCreateRequest? request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var state = new SessionState
        {
            SessionId = string.IsNullOrWhiteSpace(request?.SessionId) ? Guid.NewGuid().ToString("N") : request!.SessionId.Trim(),
            OwnerId = string.IsNullOrWhiteSpace(request?.OwnerId) ? null : request!.OwnerId.Trim(),
            Title = string.IsNullOrWhiteSpace(request?.Title) ? "新会话" : request!.Title.Trim(),
            AgentId = string.IsNullOrWhiteSpace(request?.AgentId) ? null : request!.AgentId.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        await _store.SaveAsync(state, ct);
        return CreatedAtAction(nameof(Get), new { id = state.SessionId }, state);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] SessionUpdateRequest request, CancellationToken ct)
    {
        var state = await _store.GetAsync(id, ct);
        if (state is null) return NotFound();
        if (request.Title is not null) state.Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        if (request.AgentId is not null) state.AgentId = string.IsNullOrWhiteSpace(request.AgentId) ? null : request.AgentId.Trim();
        await _store.SaveAsync(state, ct);
        return Ok(state);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await _store.DeleteAsync(id, ct);
        return NoContent();
    }
}

public sealed record SessionCreateRequest(string? SessionId, string? OwnerId, string? Title, string? AgentId);
public sealed record SessionUpdateRequest(string? Title, string? AgentId);
