using Use.Application.Service.Models.Retrieval;

namespace Use.Application.Service.Services.VectorSearch;

/// <summary>
/// Vector store abstraction used by the RAG orchestrator. The concrete
/// implementation talks to Qdrant today; tomorrow it might be swapped for
/// pgvector, Weaviate, etc.
/// </summary>
public interface IVectorSearchService
{
    Task<VectorSearchResult> SearchAsync(
        IReadOnlyList<float> queryEmbedding,
        int topK,
        CancellationToken cancellationToken);
    
    
    /// <summary>
    /// Returns every chunk that belongs to the given (sourceSystem, sourceDocumentId).
    /// No vector similarity — uses a payload filter + scroll.
    /// </summary>
    Task<IReadOnlyList<RetrievedChunk>> ListByDocumentAsync(
        string sourceSystem,
        string sourceDocumentId,
        int maxChunks,
        CancellationToken cancellationToken);
}

