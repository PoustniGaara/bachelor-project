using Use.Application.Service.Models.Requests;
using Use.Application.Service.Models.Responses;

namespace Use.Application.Service.Services.Chat;

/// <summary>
/// Application-level orchestration for the chat surface. Sits between the thin
/// <c>ChatController</c> and the RAG pipeline + persistence:
/// resolves the current user, creates/continues chat sessions, persists
/// messages, writes the <c>rag_query_log</c> / <c>rag_retrieved_chunk_log</c>
/// rows, and assembles the response.
///
/// <para>
/// Every read is scoped to the current user, so one user can never see another
/// user's sessions/messages. When Postgres is disabled, <see cref="AskAsync"/>
/// still answers (without persistence); the history/rating methods require it.
/// </para>
/// </summary>
public interface IChatApplicationService
{
    /// <summary>Run the RAG pipeline for a question, persisting the conversation + telemetry.</summary>
    Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken cancellationToken);

    /// <summary>List the current user's chat sessions (newest first).</summary>
    Task<IReadOnlyList<ChatSessionResponse>> GetSessionsAsync(CancellationToken cancellationToken);

    /// <summary>Create a new empty chat session for the current user.</summary>
    Task<ChatSessionResponse> CreateSessionAsync(string? title, CancellationToken cancellationToken);

    /// <summary>List the messages of one session owned by the current user (oldest first).</summary>
    Task<IReadOnlyList<ChatMessageResponse>> GetMessagesAsync(Guid chatSessionId, CancellationToken cancellationToken);

    /// <summary>Store a rating/feedback against a <c>rag_query_log</c> owned by the current user.</summary>
    Task<bool> RateByLogIdAsync(Guid ragQueryLogId, RatingRequest rating, CancellationToken cancellationToken);

    /// <summary>
    /// Store a rating/feedback resolved from a chat message (user or assistant);
    /// see the README note on assistant-message feedback mapping.
    /// </summary>
    Task<bool> RateByMessageAsync(Guid messageId, RatingRequest rating, CancellationToken cancellationToken);
}

