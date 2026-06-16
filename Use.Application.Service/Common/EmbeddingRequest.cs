using System.ComponentModel.DataAnnotations;

namespace Use.Application.Service.Common;

/// <summary>
/// DTO sent to the LLM service's <c>POST /api/embeddings</c> endpoint.
/// Must match <c>Use.LlmService.Api.Models.EmbeddingRequest</c>.
/// </summary>
public sealed class EmbeddingRequest
{
    [Required]
    public string Input { get; set; } = string.Empty;

    public string? SourceType { get; set; }
}

