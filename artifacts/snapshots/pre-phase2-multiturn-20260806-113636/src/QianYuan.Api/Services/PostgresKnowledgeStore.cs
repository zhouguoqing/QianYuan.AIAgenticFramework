using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using QianYuan.Core.Abstractions;
using QianYuan.Core.Models;
using QianYuan.Api.Configuration;

namespace QianYuan.Api.Services;

public sealed class PostgresKnowledgeStore : IKnowledgeStore
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly ILlmProviderRegistry _providers;
    private readonly ILogger<PostgresKnowledgeStore> _logger;
    private readonly TextVectorIndexer _index = new(128, 32);

    public PostgresKnowledgeStore(PostgresOptions options, ILlmProviderRegistry providers, ILogger<PostgresKnowledgeStore> logger)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new ArgumentException("Postgres connection string is required for the Postgres knowledge store.", nameof(options));

        _connectionString = options.ConnectionString;
        _tableName = string.IsNullOrWhiteSpace(options.TableName) ? "knowledge_documents" : options.TableName.Trim();
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<KnowledgeDocument> AddAsync(KnowledgeDocument doc, CancellationToken ct = default)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));

        doc.Vector = _index.Normalize(_index.Vectorize(doc.Content));
        await EnsureTableAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);

            var quotedTable = new NpgsqlCommandBuilder().QuoteIdentifier(_tableName);
            var sql = $@"INSERT INTO {quotedTable}
            (id, title, content, tags, source_file, source_section, created_at, vector)
            VALUES (@id, @title, @content, @tags, @source_file, @source_section, @created_at, @vector)
            ON CONFLICT (id) DO UPDATE SET
                title = EXCLUDED.title,
                content = EXCLUDED.content,
                tags = EXCLUDED.tags,
                source_file = EXCLUDED.source_file,
                source_section = EXCLUDED.source_section,
                created_at = EXCLUDED.created_at,
                vector = EXCLUDED.vector;";

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", NpgsqlDbType.Text, doc.Id);
            command.Parameters.AddWithValue("title", NpgsqlDbType.Text, doc.Title ?? string.Empty);
            command.Parameters.AddWithValue("content", NpgsqlDbType.Text, doc.Content ?? string.Empty);
            command.Parameters.AddWithValue("tags", NpgsqlDbType.Array | NpgsqlDbType.Text, doc.Tags ?? Array.Empty<string>());
            command.Parameters.AddWithValue("source_file", NpgsqlDbType.Text, doc.SourceFile ?? string.Empty);
            command.Parameters.AddWithValue("source_section", NpgsqlDbType.Text, doc.SourceSection ?? string.Empty);
            command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, doc.CreatedAt);
            command.Parameters.AddWithValue("vector", NpgsqlDbType.Array | NpgsqlDbType.Double, doc.Vector ?? Array.Empty<double>());

            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return doc;
        }
        catch (Exception ex) when (ex is OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Postgres add failed, falling back to in-memory behavior.");
            return doc;
        }
    }

    public async Task<IEnumerable<KnowledgeDocument>> ListAsync(CancellationToken ct = default)
    {
        await EnsureTableAsync(ct).ConfigureAwait(false);
        return await LoadDocumentsAsync("ORDER BY created_at DESC", ct).ConfigureAwait(false);
    }

    public async Task<KnowledgeDocument?> GetAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        await EnsureTableAsync(ct).ConfigureAwait(false);
        try
        {
            var quotedTable = new NpgsqlCommandBuilder().QuoteIdentifier(_tableName);
            var sql = $@"SELECT id, title, content, tags, source_file, source_section, created_at, vector
            FROM {quotedTable}
            WHERE id = @id LIMIT 1";

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", NpgsqlDbType.Text, id);

            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
            return ReadDocument(reader);
        }
        catch (Exception ex) when (ex is OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Postgres get failed, returning null.");
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        await EnsureTableAsync(ct).ConfigureAwait(false);
        try
        {
            var quotedTable = new NpgsqlCommandBuilder().QuoteIdentifier(_tableName);
            var sql = $@"DELETE FROM {quotedTable} WHERE id = @id";
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", NpgsqlDbType.Text, id);

            var rows = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return rows > 0;
        }
        catch (Exception ex) when (ex is OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Postgres delete failed, returning false.");
            return false;
        }
    }

    public async Task<(IEnumerable<KnowledgeDocument> matches, string? answer)> SearchAsync(string q, int topK = 5, bool answer = false, string? providerId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q)) return (Array.Empty<KnowledgeDocument>(), null);
        await EnsureTableAsync(ct).ConfigureAwait(false);

        var queryVector = _index.Normalize(_index.Vectorize(q));
        var candidates = await LoadCandidatesAsync(q, topK, ct).ConfigureAwait(false) ?? new List<KnowledgeDocument>();

        if (candidates.Count == 0)
        {
            candidates = (await LoadDocumentsAsync(string.Empty, ct).ConfigureAwait(false))?.ToList() ?? new List<KnowledgeDocument>();
        }

        var scored = candidates
            .Select(d => new { Document = d, Score = ComputeScore(queryVector, d) })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Document.CreatedAt)
            .Take(topK)
            .Select(x => x.Document)
            .ToArray();

        if (!answer) return (scored, null);

        var provider = providerId is null ? _providers.Default : _providers.Get(providerId) ?? _providers.Default;
        var docsText = new StringBuilder();
        foreach (var d in scored)
        {
            docsText.AppendLine($"Title: {d.Title}");
            if (!string.IsNullOrEmpty(d.SourceFile)) docsText.AppendLine($"Source: {d.SourceFile} {d.SourceSection}".Trim());
            docsText.AppendLine(d.Content);
            docsText.AppendLine("---");
        }

        var system = ChatMessage.System("You are a knowledge assistant. Answer concisely using only the provided documents. If the answer is not present, say you don't know and list what additional information is needed.");
        var user = ChatMessage.User($"Context:\n{docsText}\n\nQuestion: {q}");
        var req = new ChatRequest { Messages = new[] { system, user } };

        try
        {
            var resp = await provider.CompleteAsync(req, ct).ConfigureAwait(false);
            return (scored, resp.Message.AsPlainText());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate answer from provider.");
            return (scored, null);
        }
    }

    private static double ComputeScore(double[] queryVector, KnowledgeDocument document)
    {
        if (document.Vector is { Length: > 0 } vector && vector.Length == queryVector.Length)
        {
            return Dot(queryVector, vector);
        }

        var tags = document.Tags is null ? string.Empty : string.Join(' ', document.Tags);
        return KnowledgeSearch.ScoreByKeyword(document.Title + " " + document.Content + " " + tags, document);
    }

    private static double Dot(double[] a, double[] b)
    {
        var sum = 0.0;
        for (var i = 0; i < a.Length && i < b.Length; i++) sum += a[i] * b[i];
        return sum;
    }

    private static KnowledgeDocument ReadDocument(NpgsqlDataReader reader)
    {
        var result = new KnowledgeDocument
        {
            Id = reader.GetString(0),
            Title = reader.GetString(1),
            Content = reader.GetString(2),
            Tags = reader.IsDBNull(3) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(3),
            SourceFile = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            SourceSection = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            CreatedAt = reader.IsDBNull(6) ? DateTime.UtcNow : reader.GetDateTime(6),
            Vector = reader.IsDBNull(7) ? Array.Empty<double>() : reader.GetFieldValue<double[]>(7),
        };
        return result;
    }

    private async Task<List<KnowledgeDocument>> LoadCandidatesAsync(string q, int topK, CancellationToken ct)
    {
        try
        {
            var quotedTable = new NpgsqlCommandBuilder().QuoteIdentifier(_tableName);
            var sql = $@"SELECT id, title, content, tags, source_file, source_section, created_at, vector
            FROM {quotedTable}
            WHERE position(lower(@query) in lower(title)) > 0
               OR position(lower(@query) in lower(content)) > 0
            ORDER BY created_at DESC
            LIMIT @limit";

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("query", NpgsqlDbType.Text, q);
            command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, Math.Max(topK * 5, 25));

            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var results = new List<KnowledgeDocument>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                results.Add(ReadDocument(reader));
            }
            return results;
        }
        catch (Exception ex) when (ex is OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Postgres load candidates failed, returning empty list.");
            return new List<KnowledgeDocument>();
        }
    }

    private async Task<List<KnowledgeDocument>> LoadDocumentsAsync(string orderByClause, CancellationToken ct)
    {
        try
        {
            var quotedTable = new NpgsqlCommandBuilder().QuoteIdentifier(_tableName);
            var sql = $@"SELECT id, title, content, tags, source_file, source_section, created_at, vector
            FROM {quotedTable} {orderByClause}";
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var results = new List<KnowledgeDocument>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                results.Add(ReadDocument(reader));
            }
            return results;
        }
        catch (Exception ex) when (ex is OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Postgres load documents failed, returning empty list.");
            return new List<KnowledgeDocument>();
        }
    }

    private async Task EnsureTableAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            var quotedTable = new NpgsqlCommandBuilder().QuoteIdentifier(_tableName);
            var sql = $@"CREATE TABLE IF NOT EXISTS {quotedTable} (
            id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            content TEXT NOT NULL,
            tags TEXT[] NOT NULL,
            source_file TEXT NOT NULL,
            source_section TEXT NOT NULL,
            created_at TIMESTAMPTZ NOT NULL,
            vector DOUBLE PRECISION[] NOT NULL
        );";
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Postgres ensure table failed; operations will fallback.");
        }
    }
}
