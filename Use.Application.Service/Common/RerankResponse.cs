namespace Use.Application.Service.Common;

/// <summary>
/// DTO returned by the LLM service's <c>POST /api/rerank</c> endpoint.
/// Results are sorted by <see cref="RerankResult.Score"/> descending.
/// Must match <c>Use.LlmService.Api.Models.RerankResponse</c>.
/// </summary>
public sealed class RerankResponse
{
    public string Model { get; set; } = string.Empty;
    public List<RerankResult> Results { get; set; } = new();
}

