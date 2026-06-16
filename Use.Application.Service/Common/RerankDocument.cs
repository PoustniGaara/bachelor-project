namespace Use.Application.Service.Common;

/// <summary>
/// A single candidate chunk sent to the LLM service's <c>POST /api/rerank</c>
/// endpoint. Must match <c>Use.LlmService.Api.Models.RerankDocument</c>.
/// </summary>
public sealed class RerankDocument
{
    public string ChunkId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

