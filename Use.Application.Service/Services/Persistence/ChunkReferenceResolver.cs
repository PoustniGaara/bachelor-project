using Npgsql;
using NpgsqlTypes;

namespace Use.Application.Service.Services.Persistence;

/// <inheritdoc cref="IChunkReferenceResolver"/>
public sealed class ChunkReferenceResolver : IChunkReferenceResolver
{
    private readonly IPostgresDataSourceProvider _db;

    public ChunkReferenceResolver(IPostgresDataSourceProvider db) => _db = db;

    public async Task<IReadOnlyDictionary<string, ChunkReference>> ResolveAsync(
        IReadOnlyCollection<string> stableChunkIds, CancellationToken cancellationToken)
    {
        var ids = stableChunkIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
            return new Dictionary<string, ChunkReference>(StringComparer.Ordinal);

        // Single set-based lookup keyed on the stable, UNIQUE chunk_id column.
        const string sql = """
            SELECT chunk_id, id, source_document_ref_id
            FROM rag_document_chunks
            WHERE chunk_id = ANY(@ids);
            """;

        await using var cmd = _db.RequireDataSource().CreateCommand(sql);
        cmd.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = ids
        });

        var map = new Dictionary<string, ChunkReference>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var stableId = reader.GetString(0);
            map[stableId] = new ChunkReference(
                ChunkId: reader.GetGuid(1),
                DocumentId: reader.GetGuid(2));
        }

        return map;
    }
}

