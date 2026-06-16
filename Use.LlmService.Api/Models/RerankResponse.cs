namespace Use.LlmService.Api.Models;

/// <summary>
/// Provider-agnostic reranking response returned to API clients. Results are
/// sorted by <see cref="RerankResult.Score"/> descending.
/// </summary>
public sealed class RerankResponse
{
    /// <summary>Name of the model that produced the scores.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Per-chunk relevance scores, sorted by score descending.</summary>
    public List<RerankResult> Results { get; set; } = new();
}

