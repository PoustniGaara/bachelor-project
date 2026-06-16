namespace Use.Application.Service.Services.Persistence;

/// <summary>
/// Resolved SQL identity of a chunk: its <c>rag_document_chunks.id</c> and the
/// owning <c>rag_source_documents.id</c> (both UUID primary keys).
/// </summary>
public sealed record ChunkReference(Guid ChunkId, Guid DocumentId);

/// <summary>
/// Resolves stable text chunk ids (e.g. <c>WikiJs:265:7</c>, as used by Qdrant
/// and the retrieval models) to the internal SQL UUIDs required by
/// <c>rag_retrieved_chunk_log</c>.
/// </summary>
public interface IChunkReferenceResolver
{
    /// <summary>
    /// Map each resolvable stable chunk id to its <see cref="ChunkReference"/>.
    /// Unknown ids are simply absent from the result (the caller skips + warns).
    /// </summary>
    Task<IReadOnlyDictionary<string, ChunkReference>> ResolveAsync(
        IReadOnlyCollection<string> stableChunkIds, CancellationToken cancellationToken);
}

