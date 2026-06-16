using Use.Application.Service.Common;

namespace Use.Application.Service.Services.Embeddings;

/// <summary>
/// Thin abstraction over the Use.LlmService HTTP API.
/// Currently exposes embedding + generation; new capabilities can be added here.
/// </summary>
public interface ILlmServiceClient
{
    Task<EmbeddingResponse> CreateEmbeddingAsync(
        EmbeddingRequest request, CancellationToken cancellationToken);

    Task<GenerationResponse> GenerateAsync(
        GenerationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Reorders candidate chunks by their relevance to <paramref name="query"/>
    /// using the LLM service's <c>POST /api/rerank</c> endpoint. The concrete
    /// reranker model is hidden behind the gateway.
    /// </summary>
    Task<RerankResponse> RerankAsync(
        string query,
        IReadOnlyList<RerankDocument> documents,
        CancellationToken cancellationToken);
}

