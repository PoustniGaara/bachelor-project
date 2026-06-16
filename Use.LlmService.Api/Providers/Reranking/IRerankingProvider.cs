using Use.LlmService.Api.Models;

namespace Use.LlmService.Api.Providers.Reranking;

/// <summary>
/// Abstraction over a concrete reranking backend (BGE cross-encoder today,
/// Cohere / LLM-as-judge in the future). Application services depend on this
/// interface, not on a specific provider, so swapping or adding providers does
/// not affect callers.
/// </summary>
public interface IRerankingProvider
{
    /// <summary>Logical name of the provider (e.g. "Bge").</summary>
    string Name { get; }

    /// <summary>Score and reorder the supplied documents by relevance to the query.</summary>
    Task<RerankResponse> RerankAsync(
        string query,
        IReadOnlyList<RerankDocument> documents,
        CancellationToken cancellationToken = default);
}

