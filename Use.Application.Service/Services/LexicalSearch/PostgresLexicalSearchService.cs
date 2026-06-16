using Npgsql;
using NpgsqlTypes;
using Use.Application.Service.Models.Retrieval;
using Use.Application.Service.Services.Persistence;

namespace Use.Application.Service.Services.LexicalSearch;

/// <summary>
/// PostgreSQL-backed <see cref="ILexicalSearchService"/> using lightweight,
/// direct SQL via Npgsql (no EF Core). It reads the BM25-ready chunk store
/// produced by <c>Use.Indexing.Worker</c> (table <c>rag_document_chunks</c>,
/// joined to <c>rag_source_documents</c>) and ranks with PostgreSQL full-text
/// search (<c>ts_rank_cd</c>).
///
/// <para>
/// This is not a true BM25 ranker, but the chunk storage is BM25-ready and the
/// scoring here can later be replaced with a better ranker without changing the
/// retrieval contract. Scores are only used for ranking — the orchestrator
/// fuses by rank (RRF), never by raw score.
/// </para>
///
/// <para>
/// The connection pool is owned by the shared <see cref="IPostgresDataSourceProvider"/>
/// (single <c>NpgsqlDataSource</c> for the whole service), not by this class.
/// </para>
/// </summary>
public sealed class PostgresLexicalSearchService : ILexicalSearchService
{
    private readonly IPostgresDataSourceProvider _db;
    private readonly ILogger<PostgresLexicalSearchService> _logger;

    public PostgresLexicalSearchService(
        IPostgresDataSourceProvider db,
        ILogger<PostgresLexicalSearchService> logger)
    {
        _db = db;
        _logger = logger;

        if (_db.Enabled)
            _logger.LogInformation("PostgreSQL lexical search enabled — BM25-ready chunk store active.");
        else
            _logger.LogInformation("PostgreSQL lexical search disabled (Postgres not configured).");
    }

    public bool Enabled => _db.Enabled;

    public async Task<IReadOnlyList<LexicalSearchResult>> SearchAsync(
        string query,
        int limit,
        string? sourceSystem,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || !Enabled)
            return Array.Empty<LexicalSearchResult>();

        var dataSource = RequireDataSource();

        // websearch_to_tsquery is robust to arbitrary user input (quotes, Slovak
        // punctuation, etc.). If it ever fails we fall back to plainto_tsquery.
        try
        {
            return await ExecuteAsync(dataSource, "websearch_to_tsquery", query, limit, sourceSystem, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PostgresException ex)
        {
            _logger.LogWarning(ex,
                "websearch_to_tsquery failed for '{Query}'; retrying with plainto_tsquery.", query);
            return await ExecuteAsync(dataSource, "plainto_tsquery", query, limit, sourceSystem, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<LexicalSearchResult>> ExecuteAsync(
        NpgsqlDataSource dataSource,
        string tsQueryFunction,
        string query,
        int limit,
        string? sourceSystem,
        CancellationToken cancellationToken)
    {
        // The tsquery function name is a fixed constant (never user input), so
        // interpolating it here is safe; the query text is always parameterised.
        //
        // IMPORTANT: websearch_to_tsquery / plainto_tsquery combine terms with AND (&),
        // which requires a chunk to contain *every* word of the question — far too strict
        // for a natural-language query (it returns 0 rows). For lexical candidate
        // generation we want "match ANY term", so we take the safely parsed tsquery and
        // rewrite its '&' operators to '|' (OR). ts_rank_cd still ranks by how many / how
        // well the terms match; RRF + document selection + full-document expansion handle
        // precision downstream.
        var sql = $"""
                   WITH q AS (
                       SELECT NULLIF(
                           replace({tsQueryFunction}('simple', @query)::text, '&', '|'),
                           ''
                       )::tsquery AS query
                   )
                   SELECT
                       c.chunk_id,
                       c.source_system,
                       c.source_document_id,
                       c.chunk_order,
                       d.title,
                       d.source_url,
                       c.heading_path,
                       c.text,
                       ts_rank_cd(c.search_vector, q.query)::float8 AS score
                   FROM rag_document_chunks c
                   JOIN rag_source_documents d
                       ON d.id = c.source_document_ref_id
                   CROSS JOIN q
                   WHERE
                       q.query IS NOT NULL
                       AND (@sourceSystem IS NULL OR c.source_system = @sourceSystem)
                       AND c.search_vector @@ q.query
                   ORDER BY score DESC
                   LIMIT @limit;
                   """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("query", query);
        command.Parameters.Add(new NpgsqlParameter("sourceSystem", NpgsqlDbType.Text)
        {
            Value = (object?)sourceSystem ?? DBNull.Value
        });
        command.Parameters.AddWithValue("limit", Math.Max(1, limit));

        var results = new List<LexicalSearchResult>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new LexicalSearchResult(
                    ChunkId: reader.GetString(0),
                    SourceSystem: reader.GetString(1),
                    SourceDocumentId: reader.GetString(2),
                    ChunkOrder: reader.GetInt32(3),
                    Title: reader.IsDBNull(4) ? null : reader.GetString(4),
                    Url: reader.IsDBNull(5) ? null : reader.GetString(5),
                    HeadingPath: reader.IsDBNull(6) ? null : reader.GetString(6),
                    Text: reader.GetString(7),
                    Score: reader.GetDouble(8)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not PostgresException)
        {
            _logger.LogError(ex,
                "PostgreSQL lexical search failed (query='{Query}', sourceSystem={Source}). Is the database reachable?",
                query, sourceSystem ?? "<any>");
            throw;
        }

        _logger.LogDebug(
            "PostgreSQL lexical search for '{Query}' ({Source}) returned {Count} result(s).",
            query, sourceSystem ?? "<any>", results.Count);

        return results;
    }

    private NpgsqlDataSource RequireDataSource() => _db.RequireDataSource();
}

