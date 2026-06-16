using Use.Application.Service.Models.Retrieval;

namespace Use.Application.Service.Models.Responses;

/// <summary>
/// Result returned by <c>POST /api/chat</c> to the frontend.
/// </summary>
public sealed class ChatResponse
{
    /// <summary>Final natural-language answer produced by the LLM.</summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>Source documents the answer is grounded in.</summary>
    public IReadOnlyList<SourceReference> Sources { get; set; } = Array.Empty<SourceReference>();

    /// <summary>Raw retrieved chunks (with scores) — useful for debugging the UI.</summary>
    public IReadOnlyList<RetrievedChunk> RetrievedChunks { get; set; } = Array.Empty<RetrievedChunk>();

    // -----------------------------------------------------------------------
    // Chat metadata (added with SQL-backed chat history). All nullable so the
    // existing frontend contract is preserved — they are simply populated when
    // persistence is enabled, and left null when Postgres is disabled.
    // -----------------------------------------------------------------------

    /// <summary>The session this answer belongs to (new or continued).</summary>
    public Guid? ChatSessionId { get; set; }

    /// <summary>Id of the persisted user-question message.</summary>
    public Guid? UserMessageId { get; set; }

    /// <summary>Id of the persisted assistant-answer message.</summary>
    public Guid? AssistantMessageId { get; set; }

    /// <summary>Id of the <c>rag_query_log</c> row — use it to submit a rating.</summary>
    public Guid? RagQueryLogId { get; set; }
}

