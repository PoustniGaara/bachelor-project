namespace Use.Application.Service.Models.Retrieval;

/// <summary>
/// A single chunk after Reciprocal Rank Fusion (RRF) of the semantic (Qdrant)
/// and lexical (PostgreSQL) retrieval passes. Carries the fused score plus the
/// per-method rank/score telemetry so the document selector and debugging UIs
/// can reason about <em>why</em> a chunk surfaced.
/// </summary>
public sealed class FusedChunkResult
{
    public string ChunkId { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public string SourceDocumentId { get; set; } = string.Empty;
    public int ChunkOrder { get; set; }
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string? HeadingPath { get; set; }
    public string Text { get; set; } = string.Empty;

    /// <summary>Combined RRF score (sum of 1 / (k + rank) across methods).</summary>
    public double FusedScore { get; set; }

    /// <summary>1-based rank inside the semantic pass, or null if not found there.</summary>
    public int? SemanticRank { get; set; }

    /// <summary>Raw Qdrant similarity score (not comparable across methods).</summary>
    public double? SemanticScore { get; set; }

    /// <summary>1-based rank inside the lexical pass, or null if not found there.</summary>
    public int? LexicalRank { get; set; }

    /// <summary>Raw PostgreSQL ts_rank_cd score (not comparable across methods).</summary>
    public double? LexicalScore { get; set; }

    /// <summary>Best (smallest) original rank across the two methods.</summary>
    public int BestRank => Math.Min(SemanticRank ?? int.MaxValue, LexicalRank ?? int.MaxValue);

    /// <summary>
    /// Relevance score assigned by the reranker (BAAI/bge-reranker-v2-m3), or
    /// null when this chunk was not reranked (reranking disabled or chunk not in
    /// the reranked top-K).
    /// </summary>
    public double? RerankScore { get; set; }

    /// <summary>True when this chunk was scored by the reranker.</summary>
    public bool WasReranked { get; set; }

    /// <summary>1-based position of this chunk in the reranked order, or null.</summary>
    public int? RerankedRank { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Projects this fused chunk back into the normalised retrieval shape.</summary>
    public RetrievedChunk ToRetrievedChunk() => new()
    {
        ChunkId = ChunkId,
        Score = (float)FusedScore,
        Text = Text,
        SourceTitle = Title,
        SourceUrl = Url,
        SourceSystem = SourceSystem,
        SourceDocumentId = string.IsNullOrEmpty(SourceDocumentId) ? null : SourceDocumentId,
        ChunkOrder = ChunkOrder,
        Metadata = Metadata
    };
}

