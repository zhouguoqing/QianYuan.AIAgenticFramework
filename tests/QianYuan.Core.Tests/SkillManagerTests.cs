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
    public async Task SelectRelevantAsync_selects_prompt_workflow_skills_for_chinese_planning_intent()
    {
        var mgr = new SkillManager(EmptyProvider.Instance, NullLogger<SkillManager>.Instance);
        RegisterDownloadedPromptSkills(mgr);

        var picked = await mgr.SelectRelevantAsync("请评估 React 推理计划拆解，并改进代码", topK: 3);

        picked.Select(m => m.Id).Should().Contain(new[]
        {
            "agent.using.superpowers",
            "agent.brainstorming",
            "agent.brainstorm",
        });
    }

    [Fact]
    public async Task SelectRelevantAsync_selects_domain_prompt_skills_for_chinese_intents()
    {
        var mgr = new SkillManager(EmptyProvider.Instance, NullLogger<SkillManager>.Instance);
        RegisterDownloadedPromptSkills(mgr);

        var picked = await mgr.SelectRelevantAsync("从 skills.sh 查找并安装 PDF 阅读、总结和创建技能", topK: 5);

        picked.Select(m => m.Id).Should().Contain(new[]
        {
            "agent.using.superpowers",
            "agent.find.skills",
            "agent.pdf",
            "agent.skill.creator",
            "agent.summarize",
        });
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

    private static void RegisterDownloadedPromptSkills(SkillManager mgr)
    {
        mgr.Register(new FakeSkill("agent.brainstorm", "brainstorm", "Collaborative discovery and design framing for ambiguous or high-risk work", Array.Empty<string>()));
        mgr.Register(new FakeSkill("agent.brainstorming", "brainstorming", "Structured ideation before any implementation", Array.Empty<string>()));
        mgr.Register(new FakeSkill("agent.find.skills", "find-skills", "Helps users discover and install agent skills", Array.Empty<string>()));
        mgr.Register(new FakeSkill("agent.pdf", "pdf", "Read and process PDF files", Array.Empty<string>()));
        mgr.Register(new FakeSkill("agent.skill.creator", "skill-creator", "Create high-quality agent skills", Array.Empty<string>()));
        mgr.Register(new FakeSkill("agent.summarize", "summarize", "Summarize or extract content", Array.Empty<string>()));
        mgr.Register(new FakeSkill("agent.using.superpowers", "using-superpowers", "Bootstrap skill for finding and invoking skills", Array.Empty<string>()));
    }

    private sealed class EmptyProvider : IServiceProvider
    {
        public static readonly EmptyProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }
}
