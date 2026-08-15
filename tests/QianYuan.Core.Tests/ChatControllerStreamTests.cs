using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using QianYuan.Api.Controllers;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Memory;
using QianYuan.Core.Models;
using QianYuan.Core.Streaming;
using QianYuan.Data.Entities;
using QianYuan.Data.Repositories;
using QianYuan.Kernel.Agents;

namespace QianYuan.Core.Tests;

public class ChatControllerStreamTests
{
    [Fact]
    public async Task Stream_reuses_session_history_for_multi_turn_interaction()
    {
        var agent = new ScriptedAgent(
            new[]
            {
                StreamingChunk.Start(),
                StreamingChunk.OfText("first-reply"),
                StreamingChunk.End("stop")
            },
            new[]
            {
                StreamingChunk.Start(),
                StreamingChunk.OfText("second-reply"),
                StreamingChunk.End("stop")
            });

        var sessions = new TestSessionStore();
        var controller = CreateController(agent, sessions);
        var sessionId = "session-multi-turn";

        await InvokeStreamAsync(controller, new ChatStreamRequest
        {
            AgentId = agent.Id,
            SessionId = sessionId,
            OwnerId = "alice",
            UserText = "hello"
        });

        await InvokeStreamAsync(controller, new ChatStreamRequest
        {
            AgentId = agent.Id,
            SessionId = sessionId,
            OwnerId = "alice",
            UserText = "continue"
        });

        agent.Calls.Should().HaveCount(2);
        agent.Calls[0].Messages.Should().HaveCount(1);
        agent.Calls[0].Messages[0].Role.Should().Be(ChatRole.User);
        agent.Calls[0].Messages[0].AsPlainText().Should().Be("hello");

        agent.Calls[1].Messages.Should().HaveCount(3);
        agent.Calls[1].Messages[0].Role.Should().Be(ChatRole.User);
        agent.Calls[1].Messages[0].AsPlainText().Should().Be("hello");
        agent.Calls[1].Messages[1].Role.Should().Be(ChatRole.Assistant);
        agent.Calls[1].Messages[1].AsPlainText().Should().Be("first-reply");
        agent.Calls[1].Messages[2].Role.Should().Be(ChatRole.User);
        agent.Calls[1].Messages[2].AsPlainText().Should().Be("continue");

        var state = await sessions.GetAsync(sessionId);
        state.Should().NotBeNull();
        state!.Messages.Should().HaveCount(4);
    }

    [Fact]
    public async Task Stream_emits_error_event_and_persists_error_message_when_agent_throws()
    {
        var agent = new ThrowingAgent("boom-failure");
        var sessions = new TestSessionStore();
        var controller = CreateController(agent, sessions);
        var sessionId = "session-error";

        var sse = await InvokeStreamAsync(controller, new ChatStreamRequest
        {
            AgentId = agent.Id,
            SessionId = sessionId,
            OwnerId = "alice",
            UserText = "trigger error"
        });

        sse.Should().Contain("event: error");
        sse.Should().Contain("boom-failure");

        var state = await sessions.GetAsync(sessionId);
        state.Should().NotBeNull();
        state!.Messages.Should().Contain(m =>
            m.Role == ChatRole.Assistant
            && m.AsPlainText().Contains("boom-failure", StringComparison.Ordinal));
    }

    private static ChatController CreateController(IAgent agent, ISessionStore sessions)
    {
        var registry = new AgentRegistry();
        registry.Register(agent);

        var providerRegistry = new LlmProviderRegistry();
        providerRegistry.Register(new FakeProvider());

        var repo = new Mock<IAgentRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent?)null);

        return new ChatController(
            registry,
            providerRegistry,
            sessions,
            repo.Object,
            new NoopMemoryService(),
            NullLogger<ChatController>.Instance);
    }

    private static async Task<string> InvokeStreamAsync(ChatController controller, ChatStreamRequest request)
    {
        var body = new MemoryStream();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { Response = { Body = body } }
        };

        await controller.Stream(request, CancellationToken.None);

        body.Position = 0;
        using var reader = new StreamReader(body, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private sealed class ScriptedAgent : IAgent
    {
        private readonly Queue<StreamingChunk[]> _scripts;

        public ScriptedAgent(params StreamingChunk[][] scripts)
        {
            _scripts = new Queue<StreamingChunk[]>(scripts);
        }

        public string Id => "qianyuan.default";
        public string Name => "scripted";
        public string Description => "scripted";
        public IReadOnlyList<string> Tags => [];
        public List<AgentRunRequest> Calls { get; } = new();

        public async IAsyncEnumerable<StreamingChunk> RunAsync(AgentRunRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            Calls.Add(request);
            foreach (var chunk in _scripts.Dequeue())
            {
                await Task.Yield();
                yield return chunk;
            }
        }
    }

    private sealed class ThrowingAgent : IAgent
    {
        private readonly string _message;

        public ThrowingAgent(string message)
        {
            _message = message;
        }

        public string Id => "qianyuan.default";
        public string Name => "throwing";
        public string Description => "throwing";
        public IReadOnlyList<string> Tags => [];

        public async IAsyncEnumerable<StreamingChunk> RunAsync(AgentRunRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) yield break;
            await Task.Yield();
            throw new InvalidOperationException(_message);
        }
    }

    private sealed class FakeProvider : ILlmProvider
    {
        public string ProviderId => "fake";
        public string DefaultModel => "fake-model";
        public LlmCapabilities Capabilities => LlmCapabilities.Streaming;

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<StreamingChunk> StreamAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed class NoopMemoryService : IMemoryService
    {
        public ValueTask<MemorySnapshot> ReadAsync(MemoryContext context, CancellationToken ct = default)
            => ValueTask.FromResult(new MemorySnapshot(null, null, null, string.Empty, string.Empty, string.Empty));

        public ValueTask WriteMemoryAsync(MemoryContext context, string scope, string content, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask AppendDailyLogAsync(MemoryContext context, string title, string? userText, string? assistantText, CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }

    private sealed class TestSessionStore : ISessionStore
    {
        private readonly Dictionary<string, SessionState> _states = new(StringComparer.Ordinal);

        public ValueTask<SessionState?> GetAsync(string sessionId, CancellationToken ct = default)
        {
            _states.TryGetValue(sessionId, out var value);
            return ValueTask.FromResult(value);
        }

        public ValueTask SaveAsync(SessionState state, CancellationToken ct = default)
        {
            _states[state.SessionId] = state;
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(string sessionId, CancellationToken ct = default)
        {
            _states.Remove(sessionId);
            return ValueTask.CompletedTask;
        }

        public ValueTask<int> ClearAsync(string? ownerId = null, CancellationToken ct = default)
        {
            if (ownerId is null)
            {
                var count = _states.Count;
                _states.Clear();
                return ValueTask.FromResult(count);
            }

            var toDelete = _states.Values.Where(s => s.OwnerId == ownerId).Select(s => s.SessionId).ToArray();
            foreach (var id in toDelete) _states.Remove(id);
            return ValueTask.FromResult(toDelete.Length);
        }

        public ValueTask<IReadOnlyList<SessionSummary>> ListAsync(string? ownerId = null, int take = 50, CancellationToken ct = default)
        {
            IReadOnlyList<SessionSummary> list = _states.Values
                .Where(s => ownerId is null || s.OwnerId == ownerId)
                .OrderByDescending(s => s.UpdatedAt)
                .Take(take)
                .Select(s => new SessionSummary(s.SessionId, s.Title, s.AgentId, s.Messages.Count, s.CreatedAt, s.UpdatedAt))
                .ToArray();
            return ValueTask.FromResult(list);
        }
    }
}