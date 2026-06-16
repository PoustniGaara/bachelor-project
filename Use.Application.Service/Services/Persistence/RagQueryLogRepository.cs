using Npgsql;
using NpgsqlTypes;
using Use.Application.Service.Models.Logging;

namespace Use.Application.Service.Services.Persistence;

/// <inheritdoc cref="IRagQueryLogRepository"/>
public sealed class RagQueryLogRepository : IRagQueryLogRepository
{
    private readonly IPostgresDataSourceProvider _db;

    public RagQueryLogRepository(IPostgresDataSourceProvider db) => _db = db;

    public async Task<Guid> CreateAsync(
        Guid chatMessageId, string originalQuery, string retrievalStrategy, CancellationToken cancellationToken)
    {
        // started_at / created_at default to now(); answer_status defaults to
        // 'completed' and is overwritten on the completion/failure update.
        const string sql = """
            INSERT INTO rag_query_log (chat_message_id, original_query, retrieval_strategy)
            VALUES (@message, @query, @strategy)
            RETURNING id;
            """;

        await using var cmd = _db.RequireDataSource().CreateCommand(sql);
        cmd.Parameters.AddWithValue("message", chatMessageId);
        cmd.Parameters.AddWithValue("query", originalQuery);
        cmd.Parameters.AddWithValue("strategy", retrievalStrategy);

        var id = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return (Guid)id!;
    }

    public async Task MarkCompletedAsync(RagQueryLogCompletion c, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE rag_query_log
            SET augmented_query = @augmented,
                retrieval_strategy = @strategy,
                completed_at = now(),
                duration_ms = @duration,
                retrieval_duration_ms = @retrieval,
                generation_duration_ms = @generation,
                total_retrieved_chunks = @totalRetrieved,
                selected_context_chunks = @selected,
                answer_status = @status
            WHERE id = @id;
            """;

        await using var cmd = _db.RequireDataSource().CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", c.Id);
        AddNullableText(cmd, "augmented", c.AugmentedQuery);
        cmd.Parameters.AddWithValue("strategy", c.RetrievalStrategy);
        AddNullableInt(cmd, "duration", c.DurationMs);
        AddNullableInt(cmd, "retrieval", c.RetrievalDurationMs);
        AddNullableInt(cmd, "generation", c.GenerationDurationMs);
        AddNullableInt(cmd, "totalRetrieved", c.TotalRetrievedChunks);
        AddNullableInt(cmd, "selected", c.SelectedContextChunks);
        cmd.Parameters.AddWithValue("status", c.AnswerStatus);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkFailedAsync(Guid id, string? errorMessage, int? durationMs, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE rag_query_log
            SET completed_at = now(),
                duration_ms = @duration,
                answer_status = 'failed',
                error_message = @error
            WHERE id = @id;
            """;

        await using var cmd = _db.RequireDataSource().CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", id);
        AddNullableInt(cmd, "duration", durationMs);
        AddNullableText(cmd, "error", errorMessage);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkCancelledAsync(Guid id, int? durationMs, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE rag_query_log
            SET completed_at = now(),
                duration_ms = @duration,
                answer_status = 'cancelled'
            WHERE id = @id;
            """;

        await using var cmd = _db.RequireDataSource().CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", id);
        AddNullableInt(cmd, "duration", durationMs);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> UpdateRatingAsync(
        Guid ragQueryLogId, Guid userId, short? rating, string? feedback, CancellationToken cancellationToken)
    {
        // Ownership is enforced by joining log → message → session and matching the
        // session owner, so a user can only rate their own answers.
        const string sql = """
            UPDATE rag_query_log q
            SET user_rating = @rating, user_feedback = @feedback
            FROM chat_message m
            JOIN chat_session s ON s.id = m.chat_session_id
            WHERE q.id = @id
              AND q.chat_message_id = m.id
              AND s.user_id = @user;
            """;

        await using var cmd = _db.RequireDataSource().CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", ragQueryLogId);
        cmd.Parameters.AddWithValue("user", userId);
        cmd.Parameters.Add(new NpgsqlParameter("rating", NpgsqlDbType.Smallint)
        {
            Value = rating.HasValue ? rating.Value : DBNull.Value
        });
        AddNullableText(cmd, "feedback", feedback);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<Guid?> ResolveLogIdByMessageAsync(Guid messageId, Guid userId, CancellationToken cancellationToken)
    {
        // Prefer the direct match (messageId IS the referenced user message);
        // otherwise treat messageId as the assistant answer and fall back to the
        // most recent preceding user message in the same session. See README.
        const string sql = """
            WITH owned AS (
                SELECT m.id, m.chat_session_id, m.created_at
                FROM chat_message m
                JOIN chat_session s ON s.id = m.chat_session_id
                WHERE m.id = @message AND s.user_id = @user
            ),
            direct AS (
                SELECT q.id, 1 AS pref
                FROM rag_query_log q
                JOIN owned o ON o.id = q.chat_message_id
            ),
            preceding_user AS (
                SELECT um.id AS user_message_id
                FROM chat_message um
                JOIN owned o ON o.chat_session_id = um.chat_session_id
                WHERE um.role = 'user' AND um.created_at <= o.created_at
                ORDER BY um.created_at DESC, um.id DESC
                LIMIT 1
            ),
            fallback AS (
                SELECT q.id, 2 AS pref
                FROM rag_query_log q
                JOIN preceding_user pu ON pu.user_message_id = q.chat_message_id
            )
            SELECT id
            FROM (SELECT id, pref FROM direct UNION ALL SELECT id, pref FROM fallback) r
            ORDER BY pref ASC
            LIMIT 1;
            """;

        await using var cmd = _db.RequireDataSource().CreateCommand(sql);
        cmd.Parameters.AddWithValue("message", messageId);
        cmd.Parameters.AddWithValue("user", userId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is Guid id ? id : null;
    }

    private static void AddNullableInt(NpgsqlCommand cmd, string name, int? value)
        => cmd.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Integer)
        {
            Value = value.HasValue ? value.Value : DBNull.Value
        });

    private static void AddNullableText(NpgsqlCommand cmd, string name, string? value)
        => cmd.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text)
        {
            Value = (object?)value ?? DBNull.Value
        });
}

