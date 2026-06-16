namespace Use.Application.Service.Models.Retrieval;

/// <summary>
/// One retrieved candidate chunk surfaced by the orchestrator for telemetry /
/// debugging (persisted into <c>rag_retrieved_chunk_log</c>).
///
/// <para>
/// These are the <b>top candidates before document expansion</b> — i.e. the
/// fused + reranked chunks on the hybrid/lexical path, or the raw similarity
/// hits on the semantic-only path. <see cref="ChunkId"/> is the stable text id
/// (e.g. <c>WikiJs:265:7</c>); it is later resolved to the SQL chunk UUID.
/// </para>
/// </summary>
public sealed class RetrievalCandidate
{
    /// <summary>Stable text chunk id (e.g. "WikiJs:265:7").</summary>
    public string ChunkId { get; init; } = string.Empty;

    public string SourceSystem { get; init; } = string.Empty;
    public string SourceDocumentId { get; init; } = string.Empty;

    /// <summary>1-based rank after final fusion/reranking (1 = best).</summary>
    public int Rank { get; init; }

    /// <summary>Raw semantic (Qdrant) similarity score, when this chunk was a semantic hit.</summary>
    public double? SemanticScore { get; init; }

    /// <summary>Raw lexical (PostgreSQL ts_rank_cd) score, when this chunk was a lexical hit.</summary>
    public double? LexicalScore { get; init; }

    /// <summary>Reciprocal Rank Fusion (RRF) score. Null on the semantic-only path.</summary>
    public double? RrfScore { get; init; }

    /// <summary>True when this chunk ended up in the context sent to the LLM.</summary>
    public bool WasSelectedForContext { get; set; }
}

