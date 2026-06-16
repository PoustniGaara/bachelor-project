using System.ComponentModel.DataAnnotations;

namespace Use.LlmService.Api.Models;

/// <summary>
/// Request issued by the Indexing Worker (or any other client) when
/// it needs an embedding vector for a piece of text.
/// </summary>
public sealed class EmbeddingRequest
{
    /// <summary>Text that should be converted into an embedding.</summary>
    [Required]
    public string Input { get; set; } = string.Empty;

    /// <summary>
    /// Free-form hint describing what the input represents
    /// (e.g. "DocumentChunk", "UserQuery"). Used for telemetry and
    /// future routing decisions; it does not influence the model.
    /// </summary>
    public string? SourceType { get; set; }
}

