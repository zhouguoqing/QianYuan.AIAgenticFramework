using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;
using QianYuan.Kernel.Skills;

namespace QianYuan.Core.Tests;

public class SkillManagerTests
{
    [Fact]
    public void ListManifests_initially_empty()
    {
        var mgr = new SkillManager(EmptyProvider.Instance, NullLogger<SkillManager>.Instance);
        mgr.ListManifests().Should().BeEmpty();
    }

    [Fact]
    public async Task Register_skill_is_discoverable()
    {
        var mgr = new SkillManager(EmptyProvider.Instance, NullLogger<SkillManager>.Instance);
        mgr.Register(new FakeSkill("echo", "echo skill", "Echo input", new[] { "echo", "demo" }));

        mgr.ListManifests().Should().ContainSingle(m => m.Id == "echo");
        var skill = await mgr.GetAsync("echo");
        skill.Id.Should().Be("echo");
    }

    [Fact]
    public async Task Progressive_loading_via_factory_defers_construction()
    {
        var ctor = 0;
        var mgr = new SkillManager(EmptyProvider.Instance, NullLogger<SkillManager>.Instance);
        mgr.Register(
            new SkillManifest("lazy", "Lazy", "lazy skill", new[] { "lazy" }, 1, false, false),
            _ => { ctor++; return new FakeSkill("lazy", "Lazy", "lazy skill", new[] { "lazy" }); });

        mgr.ListManifests().Should().ContainSingle(m => m.Id == "lazy");
        ctor.Should().Be(0); // not yet materialized

        await mgr.GetAsync("lazy");
        ctor.Should().Be(1);

        await mgr.GetAsync("lazy"); // cached
        ctor.Should().Be(1);
    }

    [Fact]
    public async Task SelectRelevantAsync_returns_top_match_by_tags()
    {
        var mgr = new SkillManager(EmptyProvider.Instance, NullLogger<SkillManager>.Instance);
        mgr.Register(new FakeSkill("search", "Search", "Search the web", new[] { "web", "search" }));
        mgr.Register(new FakeSkill("calc", "Calc", "Math calculator", new[] { "math", "compute" }));

        var picked = await mgr.SelectRelevantAsync("please search for hello", topK: 1);
        picked.Should().HaveCount(1);
        picked[0].Id.Should().Be("search");
    }

    [Fact]
    public async Task SelectRelevantAsync_falls_back_to_all_when_no_match()
    {
        var mgr = new SkillManager(EmptyProvider.Instance, NullLogger<SkillManager>.Instance);
        mgr.Register(new FakeSkill("alpha", "Alpha", "alpha skill", Array.Empty<string>()));

        var picked = await mgr.SelectRelevantAsync("unrelated text xyz", topK: 1);
        picked.Should().HaveCount(1);
        picked[0].Id.Should().Be("alpha");
    }

    [Fact]
    public async Task CollectToolsAsync_tags_skill_id()
    {
        var mgr = new SkillManager(EmptyProvider.Instance, NullLogger<SkillManager>.Instance);
        mgr.Register(new FakeSkill("s", "S", "s", Array.Empty<string>(), tools: new[]
        {
            new ToolDefinition { Name = "do", Description = "", JsonSchema = "{}" }
        }));

        var tools = await mgr.CollectToolsAsync(new[] { "s" });
        tools.Should().HaveCount(1);
        tools[0].SkillId.Should().Be("s");
    }

    private sealed class FakeSkill : ISkill
    {
        private readonly ToolDefinition[] _tools;
        public FakeSkill(string id, string name, string desc, string[] tags, ToolDefinition[]? tools = null)
        {
            Id = id; Name = name; Description = desc; Tags = tags;
            _tools = tools ?? Array.Empty<ToolDefinition>();
        }
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public IReadOnlyList<string> Tags { get; }
        public string? SystemPromptFragment => null;
        public ValueTask<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<ToolDefinition>>(_tools);
        public ValueTask<SkillInvocationResult> InvokeAsync(string toolName, string args, SkillInvocationContext ctx, CancellationToken ct = default)
            => ValueTask.FromResult(SkillInvocationResult.Ok("{}"));
    }

    private sealed class EmptyProvider : IServiceProvider
    {
        public static readonly EmptyProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }
}
