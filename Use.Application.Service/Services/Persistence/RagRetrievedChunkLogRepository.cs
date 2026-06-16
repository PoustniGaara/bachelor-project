using Npgsql;
using NpgsqlTypes;
using Use.Application.Service.Models.Logging;

namespace Use.Application.Service.Services.Persistence;

/// <inheritdoc cref="IRagRetrievedChunkLogRepository"/>
public sealed class RagRetrievedChunkLogRepository : IRagRetrievedChunkLogRepository
{
    private readonly IPostgresDataSourceProvider _db;

    public RagRetrievedChunkLogRepository(IPostgresDataSourceProvider db) => _db = db;

    public async Task<int> InsertManyAsync(
        Guid ragQueryLogId, IReadOnlyList<RagRetrievedChunkLogEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
            return 0;

        const string sql = """
            INSERT INTO rag_retrieved_chunk_log
                (rag_query_log_id, rank, document_id, chunk_id,
                 semantic_score, lexical_score, rrf_score, was_selected_for_context)
            VALUES
                (@logId, @rank, @documentId, @chunkId,
                 @semantic, @lexical, @rrf, @selected);
            """;

        await using var connection = await _db.RequireDataSource()
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var inserted = 0;
        foreach (var e in entries)
        {
            await using var cmd = new NpgsqlCommand(sql, connection, transaction);
            cmd.Parameters.AddWithValue("logId", ragQueryLogId);
            cmd.Parameters.AddWithValue("rank", e.Rank);
            cmd.Parameters.AddWithValue("documentId", e.DocumentId);
            cmd.Parameters.AddWithValue("chunkId", e.ChunkId);
            AddNullableDouble(cmd, "semantic", e.SemanticScore);
            AddNullableDouble(cmd, "lexical", e.LexicalScore);
            AddNullableDouble(cmd, "rrf", e.RrfScore);
            cmd.Parameters.AddWithValue("selected", e.WasSelectedForContext);

            inserted += await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return inserted;
    }

    private static void AddNullableDouble(NpgsqlCommand cmd, string name, double? value)
        => cmd.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Double)
        {
            Value = value.HasValue ? value.Value : DBNull.Value
        });
}

