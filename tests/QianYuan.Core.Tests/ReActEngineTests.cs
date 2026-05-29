using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Exceptions;
using QianYuan.Core.Models;
using QianYuan.Core.Streaming;
using QianYuan.Kernel.Agents;
using QianYuan.Kernel.ReAct;
using QianYuan.Kernel.Skills;

namespace QianYuan.Core.Tests;

public class ReActEngineTests
{
    [Fact]
    public async Task Engine_dispatches_tool_call_then_terminates()
    {
        var skills = new SkillManager(EmptyServices.Instance, NullLogger<SkillManager>.Instance);
        skills.Register(new EchoSkill());

        // Provider script: first turn -> call tool 'echo'; second turn -> emit text + end.
        var provider = new ScriptedProvider(
            new[]
            {
                new StreamingChunk[]
                {
                    StreamingChunk.Start(),
                    new StreamingChunk { Kind = StreamingChunkKind.ToolCallStart, ToolCallId = "t1", ToolName = "echo" },
                    new StreamingChunk { Kind = StreamingChunkKind.ToolCallEnd, ToolCallId = "t1", ToolName = "echo", ToolArgsJson = "{\"msg\":\"hi\"}" },
                    StreamingChunk.End("tool_use"),
                },
                new StreamingChunk[]
                {
                    StreamingChunk.Start(),
                    StreamingChunk.OfText("ok"),
                    StreamingChunk.End("stop"),
                }
            });

        var dispatcher = new SimpleDispatcher(skills);
        var engine = new ReActEngine(provider, skills, new AgentRegistry(), NullLogger<ReActEngine>.Instance,
            new ReActEngineOptions { MaxIterations = 5, UseProgressiveSelection = false, ExposeAgentsAsTools = false });

        var output = new List<StreamingChunk>();
        await foreach (var c in engine.RunAsync(new ReActRunRequest
        {
            InitialMessages = new[] { ChatMessage.User("say hi") },
            SessionId = "s",
            Services = EmptyServices.Instance,
            Dispatcher = dispatcher,
            PreloadSkills = new[] { "echo" }
        }))
        {
            output.Add(c);
        }

        output.Should().Contain(c => c.Kind == StreamingChunkKind.ToolObservation);
        output.Should().Contain(c => c.Kind == StreamingChunkKind.TextDelta && c.Text == "ok");
        output.Last().Kind.Should().Be(StreamingChunkKind.End);
    }

    [Fact]
    public async Task Engine_includes_active_skill_prompt_fragments_in_system_message()
    {
        var skills = new SkillManager(EmptyServices.Instance, NullLogger<SkillManager>.Instance);
        skills.Register(new PromptSkill());

        var provider = new ScriptedProvider(new[]
        {
            new StreamingChunk[]
            {
                StreamingChunk.Start(),
                StreamingChunk.OfText("done"),
                StreamingChunk.End("stop"),
            }
        });

        var engine = new ReActEngine(provider, skills, new AgentRegistry(), NullLogger<ReActEngine>.Instance,
            new ReActEngineOptions { MaxIterations = 2, UseProgressiveSelection = false, ExposeAgentsAsTools = false });

        await foreach (var _ in engine.RunAsync(new ReActRunRequest
        {
            InitialMessages = new[] { ChatMessage.User("write tests") },
            SessionId = "s",
            Services = EmptyServices.Instance,
            Dispatcher = new SimpleDispatcher(skills),
            SystemPrompt = "base prompt",
            PreloadSkills = new[] { "prompt" }
        })) { }

        provider.SeenSystemPrompts.Should().ContainSingle(s =>
            s.Contains("base prompt", StringComparison.Ordinal)
            && s.Contains("Active skills: prompt", StringComparison.Ordinal)
            && s.Contains("Always use examples before abstractions.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Engine_progressively_selects_and_injects_prompt_skills_for_chinese_planning_intent()
    {
        var skills = new SkillManager(EmptyServices.Instance, NullLogger<SkillManager>.Instance);
        skills.Register(new PromptSkill(
            "agent.using.superpowers",
            "using-superpowers",
            "Bootstrap skill for finding and invoking skills",
            "Review available skills before starting."));
        skills.Register(new PromptSkill(
            "agent.brainstorming",
            "brainstorming",
            "Structured ideation before any implementation",
            "Brainstorm before implementation."));

        var provider = new ScriptedProvider(new[]
        {
            new StreamingChunk[]
            {
                StreamingChunk.Start(),
                StreamingChunk.OfText("done"),
                StreamingChunk.End("stop"),
            }
        });

        var engine = new ReActEngine(provider, skills, new AgentRegistry(), NullLogger<ReActEngine>.Instance,
            new ReActEngineOptions { MaxIterations = 2, ProgressiveTopK = 2, ExposeAgentsAsTools = false });

        await foreach (var _ in engine.RunAsync(new ReActRunRequest
        {
            InitialMessages = new[] { ChatMessage.User("请评估 React 推理计划拆解，并改进代码") },
            SessionId = "s",
            Services = EmptyServices.Instance,
            Dispatcher = new SimpleDispatcher(skills),
        })) { }

        provider.SeenSystemPrompts.Should().ContainSingle(s =>
            s.Contains("Active skills:", StringComparison.Ordinal)
            && s.Contains("agent.using.superpowers", StringComparison.Ordinal)
            && s.Contains("agent.brainstorming", StringComparison.Ordinal)
            && s.Contains("Review available skills before starting.", StringComparison.Ordinal)
            && s.Contains("Brainstorm before implementation.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Engine_combines_preloaded_skills_with_progressive_selection()
    {
        var skills = new SkillManager(EmptyServices.Instance, NullLogger<SkillManager>.Instance);
        skills.Register(new PromptSkill(
            "skill.summarize",
            "summarize",
            "Summarize URLs and local files",
            "Summarize content when requested."));
        skills.Register(new PromptSkill(
            "agent.brainstorming",
            "brainstorming",
            "Structured ideation before any implementation",
            "Brainstorm before implementation."));

        var provider = new ScriptedProvider(new[]
        {
            new StreamingChunk[]
            {
                StreamingChunk.Start(),
                StreamingChunk.OfText("done"),
                StreamingChunk.End("stop"),
            }
        });

        var engine = new ReActEngine(provider, skills, new AgentRegistry(), NullLogger<ReActEngine>.Instance,
            new ReActEngineOptions { MaxIterations = 2, ProgressiveTopK = 2, ExposeAgentsAsTools = false });

        await foreach (var _ in engine.RunAsync(new ReActRunRequest
        {
            InitialMessages = new[] { ChatMessage.User("请先做方案设计和推理计划") },
            SessionId = "s",
            Services = EmptyServices.Instance,
            Dispatcher = new SimpleDispatcher(skills),
            PreloadSkills = new[] { "skill.summarize" },
        })) { }

        provider.SeenSystemPrompts.Should().ContainSingle(s =>
            s.Contains("skill.summarize", StringComparison.Ordinal)
            && s.Contains("agent.brainstorming", StringComparison.Ordinal)
            && s.Contains("Summarize content when requested.", StringComparison.Ordinal)
            && s.Contains("Brainstorm before implementation.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Engine_request_max_iterations_overrides_default_options()
    {
        var skills = new SkillManager(EmptyServices.Instance, NullLogger<SkillManager>.Instance);
        skills.Register(new EchoSkill());

        var provider = new ScriptedProvider(new[]
        {
            new StreamingChunk[]
            {
                StreamingChunk.Start(),
                new StreamingChunk { Kind = StreamingChunkKind.ToolCallStart, ToolCallId = "t1", ToolName = "echo" },
                new StreamingChunk { Kind = StreamingChunkKind.ToolCallEnd, ToolCallId = "t1", ToolName = "echo", ToolArgsJson = "{\"msg\":\"again\"}" },
                StreamingChunk.End("tool_use"),
            },
            new StreamingChunk[]
            {
                StreamingChunk.Start(),
                StreamingChunk.OfText("would exceed request limit"),
                StreamingChunk.End("stop"),
            }
        });

        var engine = new ReActEngine(provider, skills, new AgentRegistry(), NullLogger<ReActEngine>.Instance,
            new ReActEngineOptions { MaxIterations = 100, UseProgressiveSelection = false, ExposeAgentsAsTools = false });

        var act = async () =>
        {
            await foreach (var _ in engine.RunAsync(new ReActRunRequest
            {
                InitialMessages = new[] { ChatMessage.User("keep using a tool") },
                SessionId = "s",
                Services = EmptyServices.Instance,
                Dispatcher = new SimpleDispatcher(skills),
                PreloadSkills = new[] { "echo" },
                MaxIterations = 1,
            })) { }
        };

        var exception = await act.Should().ThrowAsync<ReActIterationLimitException>()
            .WithMessage("ReAct iteration limit (1) exceeded.");
        exception.Which.Limit.Should().Be(1);
    }

    private sealed class EchoSkill : ISkill
    {
        public string Id => "echo";
        public string Name => "Echo";
        public string Description => "Echo back";
        public IReadOnlyList<string> Tags => new[] { "echo" };
        public string? SystemPromptFragment => null;
        public ValueTask<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<ToolDefinition>>(new[]
            {
                new ToolDefinition { Name = "echo", Description = "echo", JsonSchema = "{\"type\":\"object\"}" }
            });
        public ValueTask<SkillInvocationResult> InvokeAsync(string toolName, string args, SkillInvocationContext ctx, CancellationToken ct = default)
            => ValueTask.FromResult(SkillInvocationResult.Ok($"{{\"echo\":{args}}}", "echoed"));
    }

    private sealed class PromptSkill : ISkill
    {
        private readonly string _fragment;

        public PromptSkill(
            string id = "prompt",
            string name = "Prompt",
            string description = "Prompt-only skill",
            string fragment = "Always use examples before abstractions.")
        {
            Id = id;
            Name = name;
            Description = description;
            _fragment = fragment;
        }

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public IReadOnlyList<string> Tags => new[] { "prompt" };
        public string? SystemPromptFragment => _fragment;
        public ValueTask<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<ToolDefinition>>(Array.Empty<ToolDefinition>());
        public ValueTask<SkillInvocationResult> InvokeAsync(string toolName, string args, SkillInvocationContext ctx, CancellationToken ct = default)
            => ValueTask.FromResult(SkillInvocationResult.Ok("{}"));
    }

    private sealed class SimpleDispatcher : IToolDispatcher
    {
        private readonly ISkillManager _skills;
        public SimpleDispatcher(ISkillManager skills) => _skills = skills;
        public async ValueTask<SkillInvocationResult> InvokeAsync(string toolName, string args, SkillInvocationContext context, CancellationToken ct = default)
        {
            // For the test, route "echo" -> echo skill.
            var skill = await _skills.GetAsync("echo", ct);
            return await skill.InvokeAsync(toolName, args, context, ct);
        }
    }

    private sealed class ScriptedProvider : ILlmProvider
    {
        private readonly Queue<StreamingChunk[]> _turns;
        public ScriptedProvider(IEnumerable<StreamingChunk[]> turns) => _turns = new Queue<StreamingChunk[]>(turns);
        public string ProviderId => "scripted";
        public string DefaultModel => "fake";
        public LlmCapabilities Capabilities => LlmCapabilities.Streaming | LlmCapabilities.Tools;
        public List<string> SeenSystemPrompts { get; } = new();
        public Task<ChatResponse> CompleteAsync(ChatRequest r, CancellationToken ct = default) => throw new NotImplementedException();
        public async IAsyncEnumerable<StreamingChunk> StreamAsync(ChatRequest r, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var system = r.Messages.FirstOrDefault(m => m.Role == ChatRole.System)?.AsPlainText();
            if (system is not null) SeenSystemPrompts.Add(system);
            var script = _turns.Dequeue();
            foreach (var c in script) { await Task.Yield(); yield return c; }
        }
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public static readonly EmptyServices Instance = new();
        public object? GetService(Type t) => null;
    }
}
