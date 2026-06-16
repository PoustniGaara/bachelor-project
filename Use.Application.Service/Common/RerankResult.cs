namespace Use.Application.Service.Common;

/// <summary>
/// Per-chunk relevance score returned by the LLM service's
/// <c>POST /api/rerank</c> endpoint. Must match
/// <c>Use.LlmService.Api.Models.RerankResult</c>.
/// </summary>
public sealed class RerankResult
{
    public string ChunkId { get; set; } = string.Empty;
    public double Score { get; set; }
}

