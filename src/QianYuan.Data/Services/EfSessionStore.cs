using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Memory;
using QianYuan.Core.Models;
using QianYuan.Data.Entities;

namespace QianYuan.Data.Services;

public sealed class EfSessionStore : ISessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
    private readonly QianYuanDbContext _db;
    private readonly ITokenCounter _tokenCounter;

    public EfSessionStore(QianYuanDbContext db, ITokenCounter tokenCounter)
    {
        _db = db;
        _tokenCounter = tokenCounter;
    }

    public async ValueTask<SessionState?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        var row = await _db.Conversations
            .AsNoTracking()
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == sessionId && c.Status != "Deleted", ct)
            .ConfigureAwait(false);

        return row is null ? null : ToState(row);
    }

    public async ValueTask SaveAsync(SessionState state, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var row = await _db.Conversations
            .Include(c => c.Messages)
            .Include(c => c.Turns)
            .FirstOrDefaultAsync(c => c.Id == state.SessionId, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            row = new Conversation
            {
                Id = state.SessionId,
                CreatedAt = ToUtcDateTime(state.CreatedAt),
            };
            _db.Conversations.Add(row);
        }

        row.UserId = state.OwnerId;
        row.Title = state.Title;
        row.AgentId = state.AgentId;
        row.Status = "Active";
        row.MetadataJson = SerializeMetadata(state.Metadata);
        row.UpdatedAt = now;
        state.UpdatedAt = new DateTimeOffset(now, TimeSpan.Zero);

        _db.ConversationMessages.RemoveRange(row.Messages);
        _db.ConversationTurns.RemoveRange(row.Turns);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var messageRows = new List<ConversationMessage>();
        for (var i = 0; i < state.Messages.Count; i++)
        {
            var message = state.Messages[i];
            messageRows.Add(new ConversationMessage
            {
                ConversationId = state.SessionId,
                Role = message.Role.ToString(),
                ContentJson = JsonSerializer.Serialize(message, JsonOptions),
                SortOrder = i,
                Tokens = CountMessageTokens(message),
                CreatedAt = now,
            });
        }

        if (messageRows.Count > 0)
            _db.ConversationMessages.AddRange(messageRows);

        var turnRows = BuildTurns(state.SessionId, messageRows, now);
        if (turnRows.Count > 0)
            _db.ConversationTurns.AddRange(turnRows);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        var row = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == sessionId, ct).ConfigureAwait(false);
        if (row is null) return;
        row.Status = "Deleted";
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<SessionSummary>> ListAsync(string? ownerId = null, int take = 50, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        var query = _db.Conversations.AsNoTracking().Where(c => c.Status != "Deleted");
        if (!string.IsNullOrWhiteSpace(ownerId))
            query = query.Where(c => c.UserId == ownerId);

        var rows = await query
            .OrderByDescending(c => c.UpdatedAt)
            .Take(take)
            .Select(c => new
            {
                c.Id,
                c.Title,
                c.AgentId,
                c.CreatedAt,
                c.UpdatedAt,
                MessageCount = c.Messages.Count(m => !m.IsDeleted),
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.Select(c => new SessionSummary(
            c.Id,
            c.Title,
            c.AgentId,
            c.MessageCount,
            new DateTimeOffset(DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(c.UpdatedAt, DateTimeKind.Utc)))).ToArray();
    }

    private static SessionState ToState(Conversation row)
    {
        var state = new SessionState
        {
            SessionId = row.Id,
            OwnerId = row.UserId,
            Title = row.Title,
            AgentId = row.AgentId,
            CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)),
            UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc)),
            Metadata = DeserializeMetadata(row.MetadataJson),
        };

        foreach (var message in row.Messages.Where(m => !m.IsDeleted).OrderBy(m => m.SortOrder))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<ChatMessage>(message.ContentJson, JsonOptions);
                if (parsed is not null) state.Messages.Add(parsed);
            }
            catch (JsonException)
            {
                if (!string.IsNullOrWhiteSpace(message.ContentJson))
                    state.Messages.Add(new ChatMessage
                    {
                        Role = Enum.TryParse<ChatRole>(message.Role, true, out var role) ? role : ChatRole.Assistant,
                        Parts = [ContentPart.FromText(message.ContentJson)],
                    });
            }
        }

        return state;
    }

    private int CountMessageTokens(ChatMessage message)
    {
        var total = 4 + _tokenCounter.CountText(message.AsPlainText());
        foreach (var part in message.Parts)
        {
            total += _tokenCounter.CountText(part.JsonPayload);
            if (!string.IsNullOrEmpty(part.DataUrlOrBase64))
                total += Math.Min(4096, part.DataUrlOrBase64.Length / 4);
        }
        return Math.Max(1, total);
    }

    private static List<ConversationTurn> BuildTurns(string conversationId, IReadOnlyList<ConversationMessage> messages, DateTime now)
    {
        var turns = new List<ConversationTurn>();
        Guid? lastUserId = null;
        foreach (var message in messages.OrderBy(m => m.SortOrder))
        {
            if (string.Equals(message.Role, ChatRole.User.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                lastUserId = message.Id;
                continue;
            }

            if (lastUserId is not null && string.Equals(message.Role, ChatRole.Assistant.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                turns.Add(new ConversationTurn
                {
                    ConversationId = conversationId,
                    UserMessageId = lastUserId,
                    AssistantMessageId = message.Id,
                    CreatedAt = now,
                });
                lastUserId = null;
            }
        }

        return turns;
    }

    private static string SerializeMetadata(Dictionary<string, string>? metadata) =>
        metadata is null || metadata.Count == 0 ? "{}" : JsonSerializer.Serialize(metadata, JsonOptions);

    private static Dictionary<string, string>? DeserializeMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static DateTime ToUtcDateTime(DateTimeOffset value) => value.UtcDateTime;
}
