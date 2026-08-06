using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QianYuan.Api.Models;
using QianYuan.Api.Services;

namespace QianYuan.Api.Controllers;

/// <summary>
/// Agent Store 管理和执行 API（用于 Agent 市场功能）
/// </summary>
[ApiController]
[Route("api/agent-store")]
public class AgentStoreController : ControllerBase
{
    private readonly IAgentManagementService _managementService;
    private readonly IAgentExecutionService _executionService;
    private readonly ILogger<AgentStoreController> _logger;

    public AgentStoreController(
        IAgentManagementService managementService,
        IAgentExecutionService executionService,
        ILogger<AgentStoreController> logger)
    {
        _managementService = managementService;
        _executionService = executionService;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有 Agent
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<AgentResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllAgents(CancellationToken ct)
    {
        var agents = await _managementService.GetAllAgentsAsync(ct);
        return Ok(agents);
    }

    /// <summary>
    /// 获取指定 Agent
    /// </summary>
    [HttpGet("{agentId}")]
    [ProducesResponseType(typeof(AgentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAgent(string agentId, CancellationToken ct)
    {
        var agent = await _managementService.GetAgentAsync(agentId, ct);
        if (agent == null)
            return NotFound($"Agent '{agentId}' not found");

        return Ok(agent);
    }

    /// <summary>
    /// 创建 Agent
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(AgentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAgent([FromBody] CreateAgentRequest request, CancellationToken ct)
    {
        try
        {
            var agent = await _managementService.CreateAgentAsync(request, ct);
            return CreatedAtAction(nameof(GetAgent), new { agentId = agent.Id }, agent);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// 更新 Agent
    /// </summary>
    [HttpPut("{agentId}")]
    [ProducesResponseType(typeof(AgentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateAgent(string agentId, [FromBody] CreateAgentRequest request, CancellationToken ct)
    {
        try
        {
            var agent = await _managementService.UpdateAgentAsync(agentId, request, ct);
            return Ok(agent);
        }
        catch (KeyNotFoundException)
        {
            return NotFound($"Agent '{agentId}' not found");
        }
    }

    /// <summary>
    /// 删除 Agent
    /// </summary>
    [HttpDelete("{agentId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAgent(string agentId, CancellationToken ct)
    {
        try
        {
            await _managementService.DeleteAgentAsync(agentId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound($"Agent '{agentId}' not found");
        }
    }

    /// <summary>
    /// 向 Agent 添加 Skill
    /// </summary>
    [HttpPost("{agentId}/skills")]
    [ProducesResponseType(typeof(AgentSkillResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddSkill(string agentId, [FromBody] AddSkillToAgentRequest request, CancellationToken ct)
    {
        try
        {
            var skill = await _managementService.AddSkillAsync(agentId, request, ct);
            return Created($"/api/agent-store/{agentId}/skills/{skill.Id}", skill);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// 从 Agent 移除 Skill
    /// </summary>
    [HttpDelete("{agentId}/skills/{skillId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveSkill(string agentId, int skillId, CancellationToken ct)
    {
        await _managementService.RemoveSkillAsync(agentId, skillId, ct);
        return NoContent();
    }

    /// <summary>
    /// 向 Agent 添加 MCP Server
    /// </summary>
    [HttpPost("{agentId}/mcp-servers")]
    [ProducesResponseType(typeof(AgentMcpServerResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddMcpServer(string agentId, [FromBody] AddMcpServerToAgentRequest request, CancellationToken ct)
    {
        try
        {
            var server = await _managementService.AddMcpServerAsync(agentId, request, ct);
            return Created($"/api/agent-store/{agentId}/mcp-servers/{server.Id}", server);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// 从 Agent 移除 MCP Server
    /// </summary>
    [HttpDelete("{agentId}/mcp-servers/{serverId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveMcpServer(string agentId, int serverId, CancellationToken ct)
    {
        await _managementService.RemoveMcpServerAsync(agentId, serverId, ct);
        return NoContent();
    }

    /// <summary>
    /// 向 Agent 添加 CLI Service
    /// </summary>
    [HttpPost("{agentId}/cli-services")]
    [ProducesResponseType(typeof(AgentCliServiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddCliService(string agentId, [FromBody] AddCliServiceToAgentRequest request, CancellationToken ct)
    {
        try
        {
            var service = await _managementService.AddCliServiceAsync(agentId, request, ct);
            return Created($"/api/agent-store/{agentId}/cli-services/{service.Id}", service);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// 从 Agent 移除 CLI Service
    /// </summary>
    [HttpDelete("{agentId}/cli-services/{serviceId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveCliService(string agentId, int serviceId, CancellationToken ct)
    {
        await _managementService.RemoveCliServiceAsync(agentId, serviceId, ct);
        return NoContent();
    }

    /// <summary>
    /// 获取 Agent 可用的所有工具
    /// </summary>
    [HttpGet("{agentId}/tools")]
    [ProducesResponseType(typeof(List<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTools(string agentId, CancellationToken ct)
    {
        try
        {
            var tools = await _executionService.GetToolsAsync(agentId, ct);
            return Ok(tools);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// 测试单个工具调用
    /// </summary>
    [HttpPost("{agentId}/test-tool")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> TestTool(string agentId, [FromBody] ToolCallRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _executionService.CallToolAsync(agentId, request, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool call failed");
            return StatusCode(500, $"Tool call failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 与 Agent 互动（流式）
    /// </summary>
    [HttpPost("{agentId}/interact")]
    public async IAsyncEnumerable<object> Interact(
        string agentId,
        [FromBody] InteractRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var chunk in _executionService.InteractAsync(agentId, request.Message, ct))
        {
            yield return JsonSerializer.Deserialize<object>(chunk) ?? new { };
        }
    }
}

/// <summary>
/// 交互请求
/// </summary>
public class InteractRequest
{
    public string Message { get; set; } = string.Empty;
}
