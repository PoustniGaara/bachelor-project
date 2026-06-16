using Use.Application.Service.Models.Logging;

namespace Use.Application.Service.Services.Persistence;

/// <summary>
/// Data access for the <c>rag_query_log</c> table — one row per RAG execution.
///
/// <para>
/// The row is created when the pipeline starts and updated on completion /
/// failure / cancellation. <c>chat_message_id</c> always points at the
/// <b>user-question</b> message (the schema has no assistant-message column).
/// </para>
/// </summary>
public interface IRagQueryLogRepository
{
    /// <summary>Insert the initial log row when RAG execution starts. Returns the new id.</summary>
    Task<Guid> CreateAsync(
        Guid chatMessageId, string originalQuery, string retrievalStrategy, CancellationToken cancellationToken);

    /// <summary>Finalise the row after a successful answer / graceful fallback.</summary>
    Task MarkCompletedAsync(RagQueryLogCompletion completion, CancellationToken cancellationToken);

    /// <summary>Finalise the row after the pipeline threw.</summary>
    Task MarkFailedAsync(Guid id, string? errorMessage, int? durationMs, CancellationToken cancellationToken);

    /// <summary>Finalise the row after cancellation.</summary>
    Task MarkCancelledAsync(Guid id, int? durationMs, CancellationToken cancellationToken);

    /// <summary>
    /// Store <c>user_rating</c> / <c>user_feedback</c> for a log owned (through the
    /// chat session) by <paramref name="userId"/>. Returns true when a row changed.
    /// </summary>
    Task<bool> UpdateRatingAsync(
        Guid ragQueryLogId, Guid userId, short? rating, string? feedback, CancellationToken cancellationToken);

    /// <summary>
    /// Resolve the owning <c>rag_query_log</c> id for a chat message. The message
    /// may be the user-question message the log references directly, or — because
    /// the schema does not store the assistant-message id — the assistant answer,
    /// in which case the immediately preceding user message in the same session is
    /// used. Returns null when no owned log can be found.
    /// </summary>
    Task<Guid?> ResolveLogIdByMessageAsync(Guid messageId, Guid userId, CancellationToken cancellationToken);
}

