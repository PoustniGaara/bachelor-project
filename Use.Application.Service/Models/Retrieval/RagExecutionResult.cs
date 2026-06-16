using Use.Application.Service.Configuration;
using Use.Application.Service.Models.Logging;
using Use.Application.Service.Models.Responses;

namespace Use.Application.Service.Models.Retrieval;

/// <summary>
/// Result of one RAG pipeline run, enriched with telemetry so the chat
/// application service can persist a <c>rag_query_log</c> +
/// <c>rag_retrieved_chunk_log</c> rows without coupling the orchestrator to
/// persistence.
///
/// <para>
/// The public <see cref="Response"/> is the unchanged <see cref="ChatResponse"/>
/// the frontend already consumes; everything else is metadata for logging.
/// </para>
/// </summary>
public sealed class RagExecutionResult
{
    /// <summary>The answer payload returned to the caller.</summary>
    public required ChatResponse Response { get; init; }

    /// <summary>The retrieval mode actually used (after the Postgres degrade policy).</summary>
    public RetrievalMode Mode { get; init; }

    /// <summary>Strategy name persisted in <c>rag_query_log.retrieval_strategy</c>.</summary>
    public string RetrievalStrategy { get; init; } = string.Empty;

    /// <summary>
    /// Query after rewriting / HyDE / expansion. Always null today — the pipeline
    /// does not yet rewrite the query. (TODO: surface once query rewriting lands.)
    /// </summary>
    public string? AugmentedQuery { get; init; }

    /// <summary>Total candidate chunks retrieved before document selection (fused/semantic pool size).</summary>
    public int? TotalRetrievedChunks { get; init; }

    /// <summary>Number of chunks actually fed to the LLM (<c>Response.RetrievedChunks.Count</c>).</summary>
    public int SelectedContextChunks { get; init; }

    /// <summary>One of <see cref="RagAnswerStatus"/>.</summary>
    public string AnswerStatus { get; init; } = RagAnswerStatus.Completed;

    /// <summary>Retrieval-only duration (retrieval + fusion + rerank). Best-effort.</summary>
    public int? RetrievalDurationMs { get; init; }

    /// <summary>LLM generation-only duration. Best-effort; null when no generation ran.</summary>
    public int? GenerationDurationMs { get; init; }

    /// <summary>
    /// Top retrieved candidates (≤ 30) before document expansion, for
    /// <c>rag_retrieved_chunk_log</c>. Each carries its scores, rank and whether
    /// it ended up in the LLM context.
    /// </summary>
    public IReadOnlyList<RetrievalCandidate> Candidates { get; init; } = Array.Empty<RetrievalCandidate>();
}

