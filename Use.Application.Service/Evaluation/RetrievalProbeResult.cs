using Use.Application.Service.Configuration;

namespace Use.Application.Service.Evaluation;

/// <summary>A single candidate chunk surfaced at one retrieval stage.</summary>
public sealed record ProbeCandidate(
    string ChunkId,
    string SourceSystem,
    string SourceDocumentId,
    int Rank);

/// <summary>A document selected for context expansion.</summary>
public sealed record ProbeDocument(
    string SourceSystem,
    string SourceDocumentId,
    string? Title,
    string? Url);

/// <summary>
/// Detailed per-stage output of a <b>retrieval-only</b> pipeline run. Produced by
/// <see cref="IRetrievalProbe.ProbeRetrievalAsync"/>. The probe reuses the exact
/// same retrieval/fusion/rerank/selection/assembly services as the normal chat
/// pipeline but stops before prompt building and LLM generation.
/// </summary>
public sealed class RetrievalProbeResult
{
    /// <summary>The retrieval mode actually used (after the Postgres degrade policy).</summary>
    public RetrievalMode Mode { get; init; }

    /// <summary>True when the reranker actually ran for this probe.</summary>
    public bool RerankingApplied { get; init; }

    /// <summary>Semantic (Qdrant) candidates, ordered by their semantic rank.</summary>
    public IReadOnlyList<ProbeCandidate> SemanticCandidates { get; init; } = Array.Empty<ProbeCandidate>();

    /// <summary>Lexical (PostgreSQL) candidates, ordered by their lexical rank.</summary>
    public IReadOnlyList<ProbeCandidate> LexicalCandidates { get; init; } = Array.Empty<ProbeCandidate>();

    /// <summary>Fused (RRF) candidates, ordered by fused score.</summary>
    public IReadOnlyList<ProbeCandidate> FusedCandidates { get; init; } = Array.Empty<ProbeCandidate>();

    /// <summary>Reranked candidates, ordered after the reranker pass.</summary>
    public IReadOnlyList<ProbeCandidate> RerankedCandidates { get; init; } = Array.Empty<ProbeCandidate>();

    /// <summary>The parent documents selected for context expansion, in priority order.</summary>
    public IReadOnlyList<ProbeDocument> SelectedDocuments { get; init; } = Array.Empty<ProbeDocument>();

    /// <summary>The final assembled context chunks fed (in normal runs) to the prompt.</summary>
    public IReadOnlyList<ProbeCandidate> FinalContextChunks { get; init; } = Array.Empty<ProbeCandidate>();
}

