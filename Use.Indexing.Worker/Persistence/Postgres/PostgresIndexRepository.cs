using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Use.Indexing.Worker.Configuration;
using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Persistence.Postgres;

/// <summary>
/// PostgreSQL-backed <see cref="ISqlChunkRepository"/> using lightweight, direct
/// SQL via Npgsql (no EF Core). It stores source documents and their chunks for
/// lexical / BM25-style retrieval through PostgreSQL full-text search, keeping
/// the same chunk ids used by the Qdrant vector store.
///
/// <para>
/// Like <see cref="QdrantVectorStore"/>, schema initialization is lazy and
/// idempotent. Per-document persistence runs in a single transaction with
/// delete-then-insert replace semantics so reindexing never duplicates rows.
/// </para>
/// </summary>
public sealed class PostgresIndexRepository : ISqlChunkRepository, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    // Keys already mapped to dedicated columns — excluded from the JSONB blob
    // to avoid duplicating data (and large text) inside metadata.
    private static readonly HashSet<string> ChunkColumnKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "title", "path", "description", "headingPath", "headingLevel",
        "blockKinds", "chunkOrder", "createdAt", "updatedAt"
    };

    private readonly PostgresOptions _options;
    private readonly ISearchTextBuilder _searchTextBuilder;
    private readonly ILogger<PostgresIndexRepository> _logger;
    private readonly NpgsqlDataSource? _dataSource;

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _schemaReady;

    public PostgresIndexRepository(
        IOptions<IndexingOptions> options,
        ISearchTextBuilder searchTextBuilder,
        ILogger<PostgresIndexRepository> logger)
    {
        _options = options.Value.Postgres;
        _searchTextBuilder = searchTextBuilder;
        _logger = logger;

        if (!_options.Enabled)
        {
            _logger.LogInformation("PostgreSQL lexical store disabled by configuration.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            // Enabled but misconfigured: fail loudly rather than silently skip.
            _logger.LogError(
                "PostgreSQL lexical store is enabled but Indexing:Postgres:ConnectionString is empty. " +
                "Set it via appsettings.Development.json, user-secrets, or the " +
                "Indexing__Postgres__ConnectionString environment variable.");
            return;
        }

        _dataSource = NpgsqlDataSource.Create(_options.ConnectionString);
        _logger.LogInformation("PostgreSQL persistence enabled — lexical/BM25 chunk store active.");
    }

    public bool Enabled => _options.Enabled;

    public async Task ReplaceDocumentAsync(
        NormalizedDocument document,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken)
    {
        if (!Enabled) return;
        var dataSource = RequireDataSource();

        await EnsureSchemaAsync(dataSource, cancellationToken).ConfigureAwait(false);

        var sourceSystem = document.Reference.SourceSystem.ToString();
        var sourceDocumentId = document.Reference.SourceDocumentId;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var documentRefId = await UpsertSourceDocumentAsync(connection, document, cancellationToken)
                .ConfigureAwait(false);

            await DeleteChunksAsync(connection, sourceSystem, sourceDocumentId, cancellationToken)
                .ConfigureAwait(false);

            foreach (var chunk in chunks)
                await InsertChunkAsync(connection, documentRefId, document, chunk, cancellationToken)
                    .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "PostgreSQL: upserted document {Source}:{DocId} and replaced chunks ({Count} inserted).",
                sourceSystem, sourceDocumentId, chunks.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError(ex,
                "PostgreSQL persistence failed for {Source}:{DocId}; transaction rolled back.",
                sourceSystem, sourceDocumentId);
            throw;
        }
    }

    public async Task<IReadOnlyList<LexicalChunkSearchResult>> SearchAsync(
        string sourceSystem,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!Enabled) return Array.Empty<LexicalChunkSearchResult>();
        var dataSource = RequireDataSource();

        await EnsureSchemaAsync(dataSource, cancellationToken).ConfigureAwait(false);

        const string sql = """
            SELECT
                c.chunk_id,
                c.source_system,
                c.source_document_id,
                c.chunk_order,
                d.title,
                c.heading_path,
                c.text,
                ts_rank_cd(c.search_vector, plainto_tsquery('simple', @query)) AS rank
            FROM rag_document_chunks c
            JOIN rag_source_documents d
                ON d.id = c.source_document_ref_id
            WHERE c.source_system = @sourceSystem
              AND c.search_vector @@ plainto_tsquery('simple', @query)
            ORDER BY rank DESC
            LIMIT @limit;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("query", query);
        command.Parameters.AddWithValue("sourceSystem", sourceSystem);
        command.Parameters.AddWithValue("limit", Math.Max(1, limit));

        var results = new List<LexicalChunkSearchResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new LexicalChunkSearchResult(
                ChunkId: reader.GetString(0),
                SourceSystem: reader.GetString(1),
                SourceDocumentId: reader.GetString(2),
                ChunkOrder: reader.GetInt32(3),
                Title: reader.GetString(4),
                HeadingPath: reader.IsDBNull(5) ? null : reader.GetString(5),
                Text: reader.GetString(6),
                Rank: reader.GetDouble(7)));
        }

        _logger.LogInformation(
            "PostgreSQL lexical search ({Source}) for '{Query}' returned {Count} result(s).",
            sourceSystem, query, results.Count);

        return results;
    }

    public async ValueTask DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync().ConfigureAwait(false);
        _initLock.Dispose();
    }

    // ---------- private ----------

    private NpgsqlDataSource RequireDataSource()
        => _dataSource ?? throw new InvalidOperationException(
            "PostgreSQL lexical store is enabled but not configured " +
            "(missing/invalid Indexing:Postgres:ConnectionString).");

    private async Task<Guid> UpsertSourceDocumentAsync(
        NpgsqlConnection connection,
        NormalizedDocument document,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO rag_source_documents
                (source_system, source_document_id, title, description, path, source_url,
                 source_created_at, source_updated_at, indexed_at, metadata)
            VALUES
                (@source_system, @source_document_id, @title, @description, @path, @source_url,
                 @source_created_at, @source_updated_at, now(), @metadata)
            ON CONFLICT (source_system, source_document_id) DO UPDATE SET
                title             = EXCLUDED.title,
                description       = EXCLUDED.description,
                path              = EXCLUDED.path,
                source_url        = EXCLUDED.source_url,
                source_created_at = EXCLUDED.source_created_at,
                source_updated_at = EXCLUDED.source_updated_at,
                indexed_at        = now(),
                metadata          = EXCLUDED.metadata
            RETURNING id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("source_system", document.Reference.SourceSystem.ToString());
        command.Parameters.AddWithValue("source_document_id", document.Reference.SourceDocumentId);
        command.Parameters.AddWithValue("title", ResolveTitle(document));
        AddNullableText(command, "description", Meta(document.Metadata, "description"));
        AddNullableText(command, "path", Meta(document.Metadata, "path"));
        AddNullableText(command, "source_url", ResolveSourceUrl(document));
        AddNullableTimestamp(command, "source_created_at", ParseTimestamp(Meta(document.Metadata, "createdAt")));
        AddNullableTimestamp(command, "source_updated_at", ResolveUpdatedAt(document));
        AddJsonb(command, "metadata", BuildDocumentMetadataJson(document));

        var id = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return (Guid)id!;
    }

    private static async Task DeleteChunksAsync(
        NpgsqlConnection connection,
        string sourceSystem,
        string sourceDocumentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE FROM rag_document_chunks
            WHERE source_system = @source_system
              AND source_document_id = @source_document_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("source_system", sourceSystem);
        command.Parameters.AddWithValue("source_document_id", sourceDocumentId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertChunkAsync(
        NpgsqlConnection connection,
        Guid documentRefId,
        NormalizedDocument document,
        DocumentChunk chunk,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO rag_document_chunks
                (chunk_id, source_document_ref_id, source_system, source_document_id,
                 chunk_order, heading_path, heading_level, block_kinds,
                 text, search_text, permission_scope, access_metadata,
                 source_created_at, source_updated_at, indexed_at, metadata)
            VALUES
                (@chunk_id, @source_document_ref_id, @source_system, @source_document_id,
                 @chunk_order, @heading_path, @heading_level, @block_kinds,
                 @text, @search_text, @permission_scope, @access_metadata,
                 @source_created_at, @source_updated_at, now(), @metadata);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("chunk_id", chunk.ChunkId);
        command.Parameters.AddWithValue("source_document_ref_id", documentRefId);
        command.Parameters.AddWithValue("source_system", chunk.Reference.SourceSystem.ToString());
        command.Parameters.AddWithValue("source_document_id", chunk.Reference.SourceDocumentId);
        command.Parameters.AddWithValue("chunk_order", ResolveChunkOrder(chunk));
        AddNullableText(command, "heading_path", NullIfEmpty(Meta(chunk.Metadata, "headingPath")));
        AddNullableInt(command, "heading_level", ParseInt(Meta(chunk.Metadata, "headingLevel")));
        AddTextArray(command, "block_kinds", ParseBlockKinds(Meta(chunk.Metadata, "blockKinds")));
        command.Parameters.AddWithValue("text", chunk.Text);
        command.Parameters.AddWithValue("search_text", _searchTextBuilder.Build(chunk));
        AddNullableText(command, "permission_scope", null);
        AddJsonb(command, "access_metadata", BuildAccessMetadataJson(document));
        AddNullableTimestamp(command, "source_created_at", ParseTimestamp(Meta(document.Metadata, "createdAt")));
        AddNullableTimestamp(command, "source_updated_at", ResolveUpdatedAt(document));
        AddJsonb(command, "metadata", BuildChunkMetadataJson(chunk));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureSchemaAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        if (_schemaReady || !_options.EnsureSchema) return;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_schemaReady) return;

            await using var command = dataSource.CreateCommand(SchemaSql);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _schemaReady = true;
            _logger.LogInformation("PostgreSQL schema verified/created (rag_source_documents, rag_document_chunks).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure PostgreSQL schema. Is the database reachable?");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ---------- mapping helpers ----------

    private static string ResolveTitle(NormalizedDocument document)
        => !string.IsNullOrWhiteSpace(document.Title) ? document.Title : document.Reference.Title;

    private static string? ResolveSourceUrl(NormalizedDocument document)
    {
        var url = Meta(document.Metadata, "sourceUrl") ?? Meta(document.Metadata, "url");
        return NullIfEmpty(!string.IsNullOrWhiteSpace(url) ? url : document.Reference.Url);
    }

    private static DateTimeOffset? ResolveUpdatedAt(NormalizedDocument document)
        => ParseTimestamp(Meta(document.Metadata, "updatedAt")) ?? document.Reference.LastModified;

    private static int ResolveChunkOrder(DocumentChunk chunk)
        => ParseInt(Meta(chunk.Metadata, "chunkOrder")) ?? chunk.Order;

    /// <summary>
    /// Converts the chunker's comma-joined block-kind string (e.g.
    /// "Paragraph,Table") into a string array for the <c>text[]</c> column.
    /// </summary>
    private static string[]? ParseBlockKinds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var kinds = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return kinds.Length == 0 ? null : kinds;
    }

    private static string? BuildDocumentMetadataJson(NormalizedDocument document)
        => SerializeMetadata(document.Metadata, exclude: null,
            extra: document.Tags.Count > 0 ? new Dictionary<string, object?> { ["tags"] = document.Tags } : null);

    private static string? BuildChunkMetadataJson(DocumentChunk chunk)
        => SerializeMetadata(chunk.Metadata, exclude: ChunkColumnKeys, extra: null);

    private static string? BuildAccessMetadataJson(NormalizedDocument document)
    {
        if (document.Permissions is null) return null;
        var payload = new Dictionary<string, object?>
        {
            ["allowedPrincipals"] = document.Permissions.AllowedPrincipals,
            ["deniedPrincipals"] = document.Permissions.DeniedPrincipals
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string? SerializeMetadata(
        IReadOnlyDictionary<string, string> metadata,
        ISet<string>? exclude,
        IReadOnlyDictionary<string, object?>? extra)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var kv in metadata)
        {
            if (exclude is not null && exclude.Contains(kv.Key)) continue;
            if (string.IsNullOrEmpty(kv.Value)) continue;
            payload[kv.Key] = kv.Value;
        }

        if (extra is not null)
            foreach (var kv in extra)
                payload[kv.Key] = kv.Value;

        return payload.Count == 0 ? null : JsonSerializer.Serialize(payload, JsonOptions);
    }

    // ---------- parameter helpers ----------

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text)
        {
            Value = (object?)value ?? DBNull.Value
        });

    private static void AddNullableInt(NpgsqlCommand command, string name, int? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Integer)
        {
            Value = (object?)value ?? DBNull.Value
        });

    private static void AddNullableTimestamp(NpgsqlCommand command, string name, DateTimeOffset? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.TimestampTz)
        {
            Value = (object?)value ?? DBNull.Value
        });

    private static void AddTextArray(NpgsqlCommand command, string name, string[]? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = (object?)value ?? DBNull.Value
        });

    private static void AddJsonb(NpgsqlCommand command, string name, string? json)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = (object?)json ?? DBNull.Value
        });

    private static string? Meta(IReadOnlyDictionary<string, string> metadata, string key)
        => metadata.TryGetValue(key, out var v) ? v : null;

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static int? ParseInt(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    private static DateTimeOffset? ParseTimestamp(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var ts)
            ? ts
            : null;

    // ---------- schema ----------

    // Idempotent (IF NOT EXISTS) version of the project's PostgreSQL schema
    // (canonical DDL: ../use-sql-db/create.sql). The two metadata GIN indexes
    // are intentionally named distinctly (ix_rag_chunks_metadata vs
    // ix_rag_documents_metadata) so they coexist without a name clash.
    private const string SchemaSql = """
        CREATE EXTENSION IF NOT EXISTS pgcrypto;

        CREATE TABLE IF NOT EXISTS rag_source_documents (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            source_system TEXT NOT NULL,
            source_document_id TEXT NOT NULL,
            title TEXT NOT NULL,
            description TEXT NULL,
            path TEXT NULL,
            source_url TEXT NULL,
            source_created_at TIMESTAMPTZ NULL,
            source_updated_at TIMESTAMPTZ NULL,
            indexed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
            metadata JSONB NULL,
            CONSTRAINT uq_rag_source_documents_source_doc
                UNIQUE (source_system, source_document_id)
        );

        CREATE TABLE IF NOT EXISTS rag_document_chunks (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            chunk_id TEXT NOT NULL UNIQUE,
            source_document_ref_id UUID NOT NULL
                REFERENCES rag_source_documents(id)
                ON DELETE CASCADE,
            source_system TEXT NOT NULL,
            source_document_id TEXT NOT NULL,
            chunk_order INTEGER NOT NULL,
            heading_path TEXT NULL,
            heading_level INTEGER NULL,
            block_kinds TEXT[] NULL,
            text TEXT NOT NULL,
            search_text TEXT NOT NULL,
            permission_scope TEXT NULL,
            access_metadata JSONB NULL,
            source_created_at TIMESTAMPTZ NULL,
            source_updated_at TIMESTAMPTZ NULL,
            indexed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
            metadata JSONB NULL,
            search_vector TSVECTOR GENERATED ALWAYS AS (
                to_tsvector('simple', search_text)
            ) STORED,
            CONSTRAINT uq_rag_document_chunks_doc_order
                UNIQUE (source_system, source_document_id, chunk_order)
        );

        CREATE INDEX IF NOT EXISTS ix_rag_chunks_source_system
            ON rag_document_chunks (source_system);

        CREATE INDEX IF NOT EXISTS ix_rag_chunks_source_document
            ON rag_document_chunks (source_system, source_document_id);

        CREATE INDEX IF NOT EXISTS ix_rag_chunks_document_order
            ON rag_document_chunks (source_document_ref_id, chunk_order);

        CREATE INDEX IF NOT EXISTS ix_rag_chunks_search_vector
            ON rag_document_chunks USING GIN (search_vector);

        CREATE INDEX IF NOT EXISTS ix_rag_chunks_metadata
            ON rag_document_chunks USING GIN (metadata);

        CREATE INDEX IF NOT EXISTS ix_rag_documents_metadata
            ON rag_source_documents USING GIN (metadata);
        """;
}

