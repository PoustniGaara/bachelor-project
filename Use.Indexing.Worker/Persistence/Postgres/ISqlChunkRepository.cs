using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Persistence.Postgres;

/// <summary>
/// Lexical / BM25-ready chunk store backed by PostgreSQL full-text search.
/// Independent of <see cref="IVectorStore"/> (Qdrant): the vector store owns
/// embeddings, this store owns the clean + searchable chunk text. Both share
/// the same deterministic chunk id ("{SourceSystem}:{SourceDocumentId}:{order}").
///
/// <para>
/// Replace semantics mirror the vector store, per source document:
/// upsert the source-document row, delete its existing chunk rows, then insert
/// the fresh chunks — all inside a single transaction so reindexing the same
/// page never duplicates rows.
/// </para>
/// </summary>
public interface ISqlChunkRepository
{
    /// <summary>True when PostgreSQL persistence is configured and active.</summary>
    bool Enabled { get; }

    /// <summary>
    /// Persists one source document and its chunks using delete-then-insert
    /// replace semantics. Passing an empty <paramref name="chunks"/> list still
    /// upserts the document and clears its chunk rows (mirrors the vector store
    /// deleting stale vectors when a document becomes empty). No-op when the
    /// store is disabled.
    /// </summary>
    Task ReplaceDocumentAsync(
        NormalizedDocument document,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs a PostgreSQL full-text search over the chunk store, ranked with
    /// <c>ts_rank_cd</c>. Intended for testing the lexical index now and for
    /// later reuse by the RAG application.
    /// </summary>
    Task<IReadOnlyList<LexicalChunkSearchResult>> SearchAsync(
        string sourceSystem,
        string query,
        int limit,
        CancellationToken cancellationToken);
}

