using FluentAssertions;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;
using QianYuan.Core.Streaming;
using QianYuan.Kernel.Agents;

namespace QianYuan.Core.Tests;

public class RegistryTests
{
    [Fact]
    public void AgentRegistry_register_and_get()
    {
        var r = new AgentRegistry();
        var agent = new FakeAgent("a1", "A1");
        r.Register(agent);
        r.Get("a1")!.Id.Should().Be("a1");
        r.Get("missing").Should().BeNull();
        r.List().Should().ContainSingle();
    }

    [Fact]
    public void AgentRegistry_rejects_duplicate_id()
    {
        var r = new AgentRegistry();
        r.Register(new FakeAgent("a", "A"));
        var act = () => r.Register(new FakeAgent("a", "A2"));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void LlmProviderRegistry_default_is_first_registered()
    {
        var r = new LlmProviderRegistry();
        r.Register(new FakeProvider("p1"));
        r.Register(new FakeProvider("p2"));
        r.Default.ProviderId.Should().Be("p1");
        r.SetDefault("p2");
        r.Default.ProviderId.Should().Be("p2");
    }

    private sealed class FakeAgent : IAgent
    {
        public FakeAgent(string id, string name) { Id = id; Name = name; }
        public string Id { get; }
        public string Name { get; }
        public string Description => "";
        public IReadOnlyList<string> Tags => Array.Empty<string>();
        public IAsyncEnumerable<StreamingChunk> RunAsync(AgentRunRequest r, CancellationToken ct = default)
            => AsyncEnumerable<StreamingChunk>.Empty;
    }

    private sealed class FakeProvider : ILlmProvider
    {
        public FakeProvider(string id) { ProviderId = id; }
        public string ProviderId { get; }
        public string DefaultModel => "x";
        public LlmCapabilities Capabilities => LlmCapabilities.Streaming;
        public Task<ChatResponse> CompleteAsync(ChatRequest r, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse { Message = ChatMessage.Assistant("") });
        public IAsyncEnumerable<StreamingChunk> StreamAsync(ChatRequest r, CancellationToken ct = default)
            => AsyncEnumerable<StreamingChunk>.Empty;
    }

    private static class AsyncEnumerable<T>
    {
        public static IAsyncEnumerable<T> Empty { get; } = new EmptyImpl();
        private sealed class EmptyImpl : IAsyncEnumerable<T>
        {
            public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
                => new Enum();
            private sealed class Enum : IAsyncEnumerator<T>
            {
                public T Current => default!;
                public ValueTask DisposeAsync() => ValueTask.CompletedTask;
                public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(false);
            }
        }
    }
}
