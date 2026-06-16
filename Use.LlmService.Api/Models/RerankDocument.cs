using System.ComponentModel.DataAnnotations;

namespace Use.LlmService.Api.Models;

/// <summary>
/// A single candidate chunk to be scored by the reranker. The
/// <see cref="ChunkId"/> is opaque to this service and is echoed back unchanged
/// so callers can map scores onto their own retrieval results.
/// </summary>
public sealed class RerankDocument
{
    /// <summary>Opaque chunk identifier (e.g. "WikiJs:265:7").</summary>
    [Required]
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>Candidate chunk text scored against the query.</summary>
    [Required]
    public string Text { get; set; } = string.Empty;
}

