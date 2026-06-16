namespace Use.Application.Service.Common;

/// <summary>
/// DTO sent to the LLM service's <c>POST /api/rerank</c> endpoint.
/// Must match <c>Use.LlmService.Api.Models.RerankRequest</c>.
/// </summary>
public sealed class RerankRequest
{
    public string Query { get; set; } = string.Empty;

    public List<RerankDocument> Documents { get; set; } = new();
}

