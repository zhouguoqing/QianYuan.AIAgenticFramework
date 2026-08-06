using System.Text.Json;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;
using QianYuan.UnifyCli.Abstractions;

namespace QianYuan.UnifyCli.Skills;

/// <summary>
/// Adapter that exposes a CLI service as a Skill, making all its methods available as tools to agents.
/// </summary>
public sealed class CliServiceSkill : ISkill
{
    private readonly ICliService _cliService;
    private IReadOnlyList<ToolDefinition>? _cachedTools;

    public string Id => $"qianyuan.cli.{_cliService.Id.ToLowerInvariant()}";
    public string Name => $"CLI: {_cliService.Name}";
    public string Description => _cliService.Description;
    public IReadOnlyList<string> Tags => _cliService.Tags;
    public string? SystemPromptFragment =>
        $"You have access to CLI service '{_cliService.Name}'. Each method is exposed as a tool. " +
        "Call them to interact with the external service. All parameters must be provided as JSON.";

    public CliServiceSkill(ICliService cliService)
    {
        _cliService = cliService ?? throw new ArgumentNullException(nameof(cliService));
    }

    public async ValueTask<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct = default)
    {
        if (_cachedTools != null)
        {
            return _cachedTools;
        }

        var methods = await _cliService.GetMethodsAsync(ct).ConfigureAwait(false);
        var tools = new List<ToolDefinition>();

        foreach (var method in methods)
        {
            tools.Add(new ToolDefinition
            {
                Name = method.Id,
                Description = method.Description,
                JsonSchema = method.ParametersSchema,
                SkillId = Id
            });
        }

        _cachedTools = tools;
        return _cachedTools;
    }

    public async ValueTask<SkillInvocationResult> InvokeAsync(
        string toolName,
        string argumentsJson,
        SkillInvocationContext context,
        CancellationToken ct = default)
    {
        var method = await _cliService.GetMethodAsync(toolName, ct).ConfigureAwait(false);
        if (method == null)
        {
            return SkillInvocationResult.Error($"CLI method '{toolName}' not found in service '{_cliService.Name}'");
        }

        try
        {
            var result = await _cliService.InvokeAsync(toolName, argumentsJson, ct).ConfigureAwait(false);
            return new SkillInvocationResult
            {
                JsonContent = result.JsonContent,
                HumanSummary = result.HumanSummary,
                IsError = result.IsError
            };
        }
        catch (Exception ex)
        {
            return SkillInvocationResult.Error($"Error invoking CLI method '{toolName}': {ex.Message}");
        }
    }
}

/// <summary>
/// Factory for creating CLI service skills.
/// </summary>
public sealed class CliServiceSkillFactory
{
    private readonly ICliServiceRegistry _registry;

    public CliServiceSkillFactory(ICliServiceRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Creates a skill for a specific CLI service.
    /// </summary>
    public async ValueTask<CliServiceSkill?> CreateSkillAsync(string serviceId, CancellationToken ct = default)
    {
        var service = await _registry.GetAsync(serviceId, ct).ConfigureAwait(false);
        if (service == null) return null;

        return new CliServiceSkill(service);
    }

    /// <summary>
    /// Creates skills for all registered CLI services.
    /// </summary>
    public async ValueTask<IReadOnlyList<CliServiceSkill>> CreateAllSkillsAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var manifests = _registry.ListManifests();
        var skills = new List<CliServiceSkill>();

        foreach (var manifest in manifests)
        {
            var service = await _registry.GetAsync(manifest.Id, ct).ConfigureAwait(false);
            if (service != null)
            {
                skills.Add(new CliServiceSkill(service));
            }
        }

        return skills;
    }
}
