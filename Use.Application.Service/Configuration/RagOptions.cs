namespace Use.Application.Service.Configuration;

/// <summary>
/// High-level RAG behaviour knobs.
/// </summary>
public sealed class RagOptions
{
    public const string SectionName = "Rag";

    /// <summary>
    /// Which retrieval strategy the orchestrator uses to gather candidate chunks:
    /// <see cref="RetrievalMode.SemanticOnly"/> (legacy Qdrant-only),
    /// <see cref="RetrievalMode.LexicalOnly"/> (PostgreSQL full-text only) or
    /// <see cref="RetrievalMode.Hybrid"/> (vector + lexical fused with RRF, default).
    /// </summary>
    public RetrievalMode RetrievalMode { get; set; } = RetrievalMode.Hybrid;

    /// <summary>How many chunks to retrieve in the initial (semantic) similarity pass.</summary>
    public int InitialTopK { get; set; } = 80;

    /// <summary>How many chunks to retrieve from the lexical (PostgreSQL) pass.</summary>
    public int LexicalTopK { get; set; } = 80;

    /// <summary>Reciprocal Rank Fusion constant (k). Higher = flatter contribution
    /// of rank position. The classic default is 60.</summary>
    public int RrfK { get; set; } = 60;

    /// <summary>How many top documents to expand into full context.</summary>
    public int TopDocuments { get; set; } = 3;

    /// <summary>Hard cap on chunks loaded per document during expansion (safety).</summary>
    public int MaxChunksPerDocument { get; set; } = 500;

    /// <summary>Maximum characters from a single chunk inlined into the prompt.</summary>
    public int MaxChunkCharacters { get; set; } = 3000;

    /// <summary>
    /// When true, the fused (RRF) chunk candidates are reordered by a dedicated
    /// reranker (BAAI/bge-reranker-v2-m3 via the LLM service) after fusion and
    /// before document selection. When false, the legacy fused-score behaviour
    /// is preserved unchanged.
    /// </summary>
    public bool RerankingEnabled { get; set; } = true;

    /// <summary>
    /// How many top fused chunk candidates are sent to the reranker. Only these
    /// candidates are scored; the rest keep their fused order.
    /// </summary>
    public int RerankTopK { get; set; } = 50;

    /// <summary>Legacy knob; kept for backwards compatibility, no longer the primary lever.</summary>
    public int TopK { get; set; } = 20;
}

