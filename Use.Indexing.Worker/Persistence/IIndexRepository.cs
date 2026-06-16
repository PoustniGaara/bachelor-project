using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Persistence;

/// <summary>
/// Stores normalized document metadata + chunk metadata. Backed in production
/// by a structured store (e.g. PostgreSQL/SQL Server). Kept separate from
/// vector storage so each can evolve independently.
/// </summary>
public interface IIndexRepository
{
    /// <summary>Returns last-indexed timestamp per source for incremental indexing.</summary>
    Task<DateTimeOffset?> GetLastIndexedAtAsync(SourceSystemType source, CancellationToken cancellationToken);

    Task UpsertDocumentAsync(NormalizedDocument document, CancellationToken cancellationToken);

    Task ReplaceChunksAsync(
        SourceDocumentReference reference,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken);

    Task SetLastIndexedAtAsync(SourceSystemType source, DateTimeOffset timestamp, CancellationToken cancellationToken);
}

/// <summary>
/// Stores embedding vectors keyed by chunk id. Backed in production by a
/// vector database (pgvector, Qdrant, Azure AI Search, ...).
/// </summary>
public interface IVectorStore
{
    Task UpsertAsync(IReadOnlyList<EmbeddedChunk> embedded, CancellationToken cancellationToken);

    Task DeleteByDocumentAsync(SourceDocumentReference reference, CancellationToken cancellationToken);
}

