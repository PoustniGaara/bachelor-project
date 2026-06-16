namespace Use.Application.Service.Models.Retrieval;

/// <summary>
/// All chunks of a single source document, ordered by <see cref="RetrievedChunk.ChunkOrder"/>.
/// </summary>
public sealed class DocumentContext
{
    public string SourceSystem { get; init; } = string.Empty;
    public string SourceDocumentId { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string? Url { get; init; }

    /// <summary>How many of the initial similarity hits pointed at this document.</summary>
    public int HitCount { get; init; }

    /// <summary>Best similarity / fused score among the hits for this document.</summary>
    public float BestScore { get; init; }

    /// <summary>
    /// Aggregate ranking score for this document (best + top-3 fused chunk scores
    /// + small hit-count bonus). Zero on the legacy semantic-only path.
    /// </summary>
    public double DocumentScore { get; init; }

    /// <summary>Chunk id of the highest-scoring (fused) chunk that selected this document.</summary>
    public string? RepresentativeChunkId { get; init; }

    public IReadOnlyList<RetrievedChunk> OrderedChunks { get; init; } = Array.Empty<RetrievedChunk>();
}