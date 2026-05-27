using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Exceptions;
using QianYuan.Core.Models;
using QianYuan.Core.Streaming;

namespace QianYuan.Kernel.ReAct;

/// <summary>
/// Generic ReAct (reason+act) loop driven by native tool calls.
///
/// Each iteration:
///   1. Build the tool list from currently-mounted skills (progressive load on the fly).
///   2. Stream a turn from the LLM, forwarding text/thinking deltas to the caller.
///   3. If the model emitted tool calls -> dispatch them, append observations, loop.
///   4. Otherwise -> emit End, terminate.
///
/// The engine is provider-agnostic: any <see cref="ILlmProvider"/> that surfaces tool calls
/// via the unified <see cref="StreamingChunk"/> contract works here.
/// </summary>
public sealed class ReActEngine
{
    private readonly ILlmProvider _llm;
    private readonly ISkillManager _skills;
    private readonly IAgentRegistry _agents;
    private readonly ILogger<ReActEngine> _logger;
    private readonly ReActEngineOptions _opts;

    public ReActEngine(
        ILlmProvider llm,
        ISkillManager skills,
        IAgentRegistry agents,
        ILogger<ReActEngine> logger,
        ReActEngineOptions? opts = null)
    {
        _llm = llm;
        _skills = skills;
        _agents = agents;
        _logger = logger;
        _opts = opts ?? new ReActEngineOptions();
    }

    public async IAsyncEnumerable<StreamingChunk> RunAsync(
        ReActRunRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var workingMessages = new List<ChatMessage>(request.InitialMessages);

        // Build the initial active skill set: explicit preloads + intent-based selection.
        var lastUserText = workingMessages.LastOrDefault(m => m.Role == ChatRole.User)?.AsPlainText() ?? "";
        var activeSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (request.PreloadSkills is not null)
            foreach (var id in request.PreloadSkills) activeSkills.Add(id);

        if (_opts.UseProgressiveSelection && activeSkills.Count == 0 && !string.IsNullOrWhiteSpace(lastUserText))
        {
            var picked = await _skills.SelectRelevantAsync(lastUserText, _opts.ProgressiveTopK, ct).ConfigureAwait(false);
            foreach (var m in picked) activeSkills.Add(m.Id);
        }

        // Always include agent-as-tool entries.
        var toolNameToSkillId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        yield return StreamingChunk.Start(_opts.Model ?? _llm.DefaultModel);

        var iteration = 0;
        var maxIter = request.MaxIterations ?? _opts.MaxIterations;
        TokenUsage? lastUsage = null;
        string? finishReason = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (iteration >= maxIter)
                throw new ReActIterationLimitException(maxIter);

            iteration++;

            // Refresh tool list from active skills each iteration so progressive expansion is picked up.
            var tools = await _skills.CollectToolsAsync(activeSkills, ct).ConfigureAwait(false);
            toolNameToSkillId.Clear();
            foreach (var t in tools)
                if (t.SkillId is not null) toolNameToSkillId[t.Name] = t.SkillId;

            // Mount registered agents as agent.<id> tools when configured.
            var allTools = new List<ToolDefinition>(tools);
            if (_opts.ExposeAgentsAsTools)
            {
                foreach (var ag in _agents.List())
                {
                    if (ag.Id == request.SelfAgentId) continue; // don't self-recurse
                    allTools.Add(new ToolDefinition
                    {
                        Name = $"agent.{ag.Id}",
                        Description = $"Delegate the task to agent '{ag.Name}'. {ag.Description}",
                        JsonSchema = """{"type":"object","properties":{"input":{"type":"string","description":"Task description for the delegated agent."}},"required":["input"]}""",
                        SkillId = null,
                    });
                }
            }

            var chatReq = new ChatRequest
            {
                Messages = BuildMessagesWithSystem(workingMessages, request.SystemPrompt, activeSkills),
                Tools = allTools.Count == 0 ? null : allTools,
                Options = new GenerationOptions
                {
                    Model = _opts.Model,
                    Temperature = _opts.Temperature,
                    MaxOutputTokens = _opts.MaxOutputTokens,
                    Stream = true,
                    ToolChoice = allTools.Count == 0 ? "none" : "auto",
                    EnablePromptCaching = true,
                }
            };

            var assistantParts = new List<ContentPart>();
            var pendingCalls = new Dictionary<string, PendingToolCall>(StringComparer.Ordinal);
            var assistantText = new StringBuilder();

            await foreach (var chunk in _llm.StreamAsync(chatReq, ct).ConfigureAwait(false))
            {
                switch (chunk.Kind)
                {
                    case StreamingChunkKind.Start:
                        // Already emitted at top.
                        break;

                    case StreamingChunkKind.TextDelta:
                        if (chunk.Text is { Length: > 0 })
                        {
                            assistantText.Append(chunk.Text);
                            yield return chunk with { AgentId = request.SelfAgentId, Step = iteration };
                        }
                        break;

                    case StreamingChunkKind.ThinkingDelta:
                        yield return chunk with { AgentId = request.SelfAgentId, Step = iteration };
                        break;

                    case StreamingChunkKind.ToolCallStart:
                        if (chunk.ToolCallId is not null)
                        {
                            pendingCalls[chunk.ToolCallId] = new PendingToolCall
                            {
                                Id = chunk.ToolCallId,
                                Name = chunk.ToolName ?? "",
                                Args = new StringBuilder(chunk.ToolArgsJson ?? "")
                            };
                            yield return chunk with { AgentId = request.SelfAgentId, Step = iteration };
                        }
                        break;

                    case StreamingChunkKind.ToolCallArgsDelta:
                        if (chunk.ToolCallId is not null && pendingCalls.TryGetValue(chunk.ToolCallId, out var cur))
                        {
                            cur.Args.Append(chunk.ToolArgsJson ?? "");
                        }
                        break;

                    case StreamingChunkKind.ToolCallEnd:
                        if (chunk.ToolCallId is not null && pendingCalls.TryGetValue(chunk.ToolCallId, out var done))
                        {
                            // If the provider supplied the full args at End time, prefer that.
                            if (!string.IsNullOrEmpty(chunk.ToolArgsJson))
                            {
                                done.Args.Clear();
                                done.Args.Append(chunk.ToolArgsJson);
                            }
                        }
                        break;

                    case StreamingChunkKind.Usage:
                        lastUsage = chunk.Usage;
                        break;

                    case StreamingChunkKind.End:
                        finishReason = chunk.FinishReason;
                        lastUsage ??= chunk.Usage;
                        break;

                    case StreamingChunkKind.Warning:
                        yield return chunk;
                        break;

                    case StreamingChunkKind.Error:
                        yield return chunk;
                        yield return StreamingChunk.End("error");
                        yield break;

                    case StreamingChunkKind.ToolObservation:
                        // Provider should not emit these - the engine does.
                        break;
                }
            }

            // Save the assistant turn into the running transcript.
            if (assistantText.Length > 0)
                assistantParts.Add(ContentPart.FromText(assistantText.ToString()));
            foreach (var (_, p) in pendingCalls)
                assistantParts.Add(ContentPart.ToolCall(p.Id, p.Name, p.Args.ToString().Trim().Length == 0 ? "{}" : p.Args.ToString()));

            if (assistantParts.Count > 0)
                workingMessages.Add(ChatMessage.Assistant(assistantParts));

            // No tool calls -> we're done.
            if (pendingCalls.Count == 0)
            {
                yield return StreamingChunk.End(finishReason ?? "stop", lastUsage);
                yield break;
            }

            // Execute tool calls (possibly in parallel) and append observations.
            var dispatch = request.Dispatcher
                ?? throw new InvalidOperationException("ReActRunRequest.Dispatcher is required when tools are used.");

            var ctx = new SkillInvocationContext
            {
                AgentId = request.SelfAgentId ?? "agent",
                SessionId = request.SessionId,
                Services = request.Services,
                Metadata = request.Metadata,
            };

            // Run sequentially by default to keep observation ordering simple; can be parallelised by setting opt.
            foreach (var (id, call) in pendingCalls)
            {
                ct.ThrowIfCancellationRequested();
                _logger.LogInformation("ReAct iter {Iter} -> tool {Tool}", iteration, call.Name);

                SkillInvocationResult result;
                try
                {
                    result = await dispatch.InvokeAsync(call.Name, call.Args.ToString(), ctx, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result = SkillInvocationResult.Error(ex.Message);
                }

                workingMessages.Add(ChatMessage.Tool(id, result.JsonContent, result.HumanSummary));

                // Build a user-visible observation: human summary if present, else the raw JSON
                // payload (truncated). This is what the Web UI surfaces under "工具结果".
                var observationText = !string.IsNullOrWhiteSpace(result.HumanSummary)
                    ? result.HumanSummary!
                    : (result.IsError ? "tool error" : "");
                if (!string.IsNullOrWhiteSpace(result.JsonContent) && result.JsonContent != "{}")
                {
                    var snippet = result.JsonContent.Length > 4000
                        ? result.JsonContent[..4000] + "\n…(truncated)"
                        : result.JsonContent;
                    observationText = string.IsNullOrEmpty(observationText)
                        ? snippet
                        : observationText + "\n\n" + snippet;
                }
                if (string.IsNullOrEmpty(observationText)) observationText = "tool ok";

                yield return StreamingChunk.Observation(observationText, id) with
                {
                    ToolName = call.Name,
                    Step = iteration,
                    AgentId = request.SelfAgentId,
                };
            }

            // Optional progressive expansion: if the user query mentioned a skill not yet active, mount it now.
            if (_opts.UseProgressiveSelection)
            {
                var more = await _skills.SelectRelevantAsync(
                    BuildIntent(workingMessages),
                    _opts.ProgressiveTopK,
                    ct).ConfigureAwait(false);
                foreach (var m in more) activeSkills.Add(m.Id);
            }
        }
    }

    private static IReadOnlyList<ChatMessage> BuildMessagesWithSystem(
        IReadOnlyList<ChatMessage> conversation,
        string? systemPrompt,
        IReadOnlyCollection<string> activeSkills)
    {
        var combined = systemPrompt;
        if (activeSkills.Count > 0)
        {
            var skillList = string.Join(", ", activeSkills);
            combined = (combined is null ? "" : combined + "\n\n") + $"Active skills: {skillList}";
        }

        if (string.IsNullOrWhiteSpace(combined)) return conversation;

        // If the conversation already starts with a system message, merge into it.
        if (conversation.Count > 0 && conversation[0].Role == ChatRole.System)
        {
            var head = conversation[0].AsPlainText();
            var merged = ChatMessage.System(head + "\n\n" + combined);
            var rest = conversation.Skip(1);
            return new[] { merged }.Concat(rest).ToArray();
        }

        return new[] { ChatMessage.System(combined) }.Concat(conversation).ToArray();
    }

    private static string BuildIntent(IReadOnlyList<ChatMessage> messages)
    {
        var last = messages.LastOrDefault(m => m.Role == ChatRole.User)?.AsPlainText();
        if (!string.IsNullOrWhiteSpace(last)) return last;
        var any = messages.LastOrDefault()?.AsPlainText();
        return any ?? "";
    }

    private sealed class PendingToolCall
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required StringBuilder Args { get; init; }
    }
}

public sealed class ReActEngineOptions
{
    public int MaxIterations { get; init; } = 10;
    public string? Model { get; init; }
    public float? Temperature { get; init; } = 0.4f;
    public int? MaxOutputTokens { get; init; }

    /// <summary>If true, the engine asks SkillManager.SelectRelevantAsync to pick skills based on the user intent.</summary>
    public bool UseProgressiveSelection { get; init; } = true;
    public int ProgressiveTopK { get; init; } = 8;

    /// <summary>If true, every other registered agent is exposed as an "agent.&lt;id&gt;" tool.</summary>
    public bool ExposeAgentsAsTools { get; init; } = true;
}

public sealed class ReActRunRequest
{
    public required IReadOnlyList<ChatMessage> InitialMessages { get; init; }
    public required string SessionId { get; init; }
    public required IServiceProvider Services { get; init; }
    public required IToolDispatcher Dispatcher { get; init; }
    public string? SelfAgentId { get; init; }
    public string? SystemPrompt { get; init; }
    public IReadOnlyList<string>? PreloadSkills { get; init; }
    public int? MaxIterations { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
