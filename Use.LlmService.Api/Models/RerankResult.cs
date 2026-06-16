namespace Use.LlmService.Api.Models;

/// <summary>
/// Relevance score for a single chunk returned by the reranker.
/// </summary>
public sealed class RerankResult
{
    /// <summary>Opaque chunk identifier echoed back from the request.</summary>
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>Relevance score (higher = more relevant). Normalised to (0, 1).</summary>
    public double Score { get; set; }
}

