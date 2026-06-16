namespace Use.Application.Service.Models.Logging;

/// <summary>
/// One RAG execution record. Mirrors the <c>rag_query_log</c> table.
///
/// <para>
/// <see cref="ChatMessageId"/> references the <b>user question</b> message that
/// triggered the pipeline (the schema does not reference the assistant answer
/// message — see README "RAG logging" for the implication on rating/feedback).
/// </para>
/// </summary>
public sealed record RagQueryLog(
    Guid Id,
    Guid ChatMessageId,
    string OriginalQuery,
    string? AugmentedQuery,
    string RetrievalStrategy,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int? DurationMs,
    int? RetrievalDurationMs,
    int? GenerationDurationMs,
    int? TotalRetrievedChunks,
    int? SelectedContextChunks,
    string AnswerStatus,
    string? ErrorMessage,
    short? UserRating,
    string? UserFeedback,
    DateTimeOffset CreatedAt);

/// <summary>
/// Values written when a RAG execution completes (or falls back). Used to update
/// the row that was created at the start of the pipeline.
/// </summary>
public sealed record RagQueryLogCompletion(
    Guid Id,
    string? AugmentedQuery,
    string RetrievalStrategy,
    int? DurationMs,
    int? RetrievalDurationMs,
    int? GenerationDurationMs,
    int? TotalRetrievedChunks,
    int? SelectedContextChunks,
    string AnswerStatus);

