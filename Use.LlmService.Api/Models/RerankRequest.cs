using System.ComponentModel.DataAnnotations;

namespace Use.LlmService.Api.Models;

/// <summary>
/// Request used by the Application Service to reorder candidate chunks by their
/// relevance to a query. Issued after hybrid retrieval + RRF fusion, before
/// document selection.
/// </summary>
public sealed class RerankRequest
{
    /// <summary>The user question the documents are scored against.</summary>
    [Required]
    public string Query { get; set; } = string.Empty;

    /// <summary>Candidate chunks to score and reorder.</summary>
    [Required]
    public List<RerankDocument> Documents { get; set; } = new();
}

