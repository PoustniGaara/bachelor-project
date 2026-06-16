namespace Use.Application.Service.Common;

/// <summary>
/// DTO returned by the LLM service's <c>POST /api/embeddings</c> endpoint.
/// Must match <c>Use.LlmService.Api.Models.EmbeddingResponse</c>.
/// </summary>
public sealed class EmbeddingResponse
{
    public string Model { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public float[] Embedding { get; set; } = Array.Empty<float>();
}

