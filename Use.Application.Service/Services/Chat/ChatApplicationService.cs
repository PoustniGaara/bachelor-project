using System.Diagnostics;
using Microsoft.Extensions.Options;
using Use.Application.Service.Configuration;
using Use.Application.Service.Models.Chat;
using Use.Application.Service.Models.Logging;
using Use.Application.Service.Models.Requests;
using Use.Application.Service.Models.Responses;
using Use.Application.Service.Models.Retrieval;
using Use.Application.Service.Services.Identity;
using Use.Application.Service.Services.Persistence;
using Use.Application.Service.Services.Rag;

namespace Use.Application.Service.Services.Chat;

/// <inheritdoc cref="IChatApplicationService"/>
public sealed class ChatApplicationService : IChatApplicationService
{
    private const string DefaultSessionTitle = "New chat";
    private const int SessionTitleMaxLength = 60;

    private readonly ICurrentUserService _currentUser;
    private readonly IChatSessionRepository _sessions;
    private readonly IChatMessageRepository _messages;
    private readonly IRagQueryLogRepository _queryLogs;
    private readonly IRagRetrievedChunkLogRepository _retrievedChunkLogs;
    private readonly IChunkReferenceResolver _chunkResolver;
    private readonly IRagOrchestrator _orchestrator;
    private readonly IPostgresDataSourceProvider _db;
    private readonly RagOptions _ragOptions;
    private readonly ILogger<ChatApplicationService> _logger;

    public ChatApplicationService(
        ICurrentUserService currentUser,
        IChatSessionRepository sessions,
        IChatMessageRepository messages,
        IRagQueryLogRepository queryLogs,
        IRagRetrievedChunkLogRepository retrievedChunkLogs,
        IChunkReferenceResolver chunkResolver,
        IRagOrchestrator orchestrator,
        IPostgresDataSourceProvider db,
        IOptions<RagOptions> ragOptions,
        ILogger<ChatApplicationService> logger)
    {
        _currentUser = currentUser;
        _sessions = sessions;
        _messages = messages;
        _queryLogs = queryLogs;
        _retrievedChunkLogs = retrievedChunkLogs;
        _chunkResolver = chunkResolver;
        _orchestrator = orchestrator;
        _db = db;
        _ragOptions = ragOptions.Value;
        _logger = logger;
    }

    public async Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var question = request.Question.Trim();
        if (string.IsNullOrEmpty(question))
            throw new ArgumentException("Question must not be empty.", nameof(request));

        // Without Postgres we cannot persist chat history; still answer the
        // question so SemanticOnly / Postgres-off local dev keeps working.
        if (!_db.Enabled)
        {
            _logger.LogWarning("Postgres disabled — answering without chat-history persistence.");
            var execNoPersistence = await _orchestrator.ExecuteAsync(question, cancellationToken).ConfigureAwait(false);
            return execNoPersistence.Response;
        }

        var user = await _currentUser.GetOrCreateCurrentUserAsync(cancellationToken).ConfigureAwait(false);

        // 1) Resolve or create the session (ownership enforced by the repository).
        ChatSession session;
        if (request.ChatSessionId is { } existingSessionId)
        {
            session = await _sessions.GetByIdAsync(existingSessionId, user.Id, cancellationToken).ConfigureAwait(false)
                      ?? throw new KeyNotFoundException(
                          $"Chat session '{existingSessionId}' was not found for the current user.");
        }
        else
        {
            session = await _sessions.CreateAsync(user.Id, BuildSessionTitle(question), cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation("Created chat session {SessionId} for user {UserId}.", session.Id, user.Id);
        }

        // 2) Persist the user question message.
        var userMessage = await _messages
            .CreateAsync(session.Id, ChatMessageRole.User, question, cancellationToken)
            .ConfigureAwait(false);

        // 3) Open the RAG log (initial strategy from config; refined on completion).
        var initialStrategy = RetrievalStrategyNames.From(_ragOptions.RetrievalMode);
        var queryLogId = await _queryLogs
            .CreateAsync(userMessage.Id, question, initialStrategy, cancellationToken)
            .ConfigureAwait(false);

        // 4) Run the pipeline, marking the log on cancellation / failure.
        var totalSw = Stopwatch.StartNew();
        RagExecutionResult execution;
        try
        {
            execution = await _orchestrator.ExecuteAsync(question, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            totalSw.Stop();
            await SafeMarkCancelledAsync(queryLogId, (int)totalSw.ElapsedMilliseconds).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            totalSw.Stop();
            await SafeMarkFailedAsync(queryLogId, ex.Message, (int)totalSw.ElapsedMilliseconds).ConfigureAwait(false);
            throw;
        }

        totalSw.Stop();

        // 5) Persist the assistant answer + bump the session timestamp.
        var assistantMessage = await _messages
            .CreateAsync(session.Id, ChatMessageRole.Assistant, execution.Response.Answer, cancellationToken)
            .ConfigureAwait(false);
        await _sessions.TouchAsync(session.Id, user.Id, cancellationToken).ConfigureAwait(false);

        // 6) Finalise the RAG log.
        await _queryLogs.MarkCompletedAsync(new RagQueryLogCompletion(
            Id: queryLogId,
            AugmentedQuery: execution.AugmentedQuery,
            RetrievalStrategy: execution.RetrievalStrategy,
            DurationMs: (int)totalSw.ElapsedMilliseconds,
            RetrievalDurationMs: execution.RetrievalDurationMs,
            GenerationDurationMs: execution.GenerationDurationMs,
            TotalRetrievedChunks: execution.TotalRetrievedChunks,
            SelectedContextChunks: execution.SelectedContextChunks,
            AnswerStatus: execution.AnswerStatus), cancellationToken).ConfigureAwait(false);

        // 7) Best-effort retrieved-chunk logging — never fail the request for it.
        await SafeLogRetrievedChunksAsync(queryLogId, execution.Candidates, cancellationToken).ConfigureAwait(false);

        // 8) Enrich the response with chat metadata for the frontend.
        var response = execution.Response;
        response.ChatSessionId = session.Id;
        response.UserMessageId = userMessage.Id;
        response.AssistantMessageId = assistantMessage.Id;
        response.RagQueryLogId = queryLogId;
        return response;
    }

    public async Task<IReadOnlyList<ChatSessionResponse>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        EnsurePersistenceAvailable();
        var user = await _currentUser.GetOrCreateCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        var sessions = await _sessions.GetByUserAsync(user.Id, cancellationToken).ConfigureAwait(false);
        return sessions.Select(ToResponse).ToList();
    }

    public async Task<ChatSessionResponse> CreateSessionAsync(string? title, CancellationToken cancellationToken)
    {
        EnsurePersistenceAvailable();
        var user = await _currentUser.GetOrCreateCurrentUserAsync(cancellationToken).ConfigureAwait(false);

        var resolvedTitle = string.IsNullOrWhiteSpace(title) ? DefaultSessionTitle : title.Trim();
        var session = await _sessions.CreateAsync(user.Id, resolvedTitle, cancellationToken).ConfigureAwait(false);
        return ToResponse(session);
    }

    public async Task<IReadOnlyList<ChatMessageResponse>> GetMessagesAsync(
        Guid chatSessionId, CancellationToken cancellationToken)
    {
        EnsurePersistenceAvailable();
        var user = await _currentUser.GetOrCreateCurrentUserAsync(cancellationToken).ConfigureAwait(false);

        // Confirm ownership first so we can return 404 for "not found / not owned".
        _ = await _sessions.GetByIdAsync(chatSessionId, user.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException(
                $"Chat session '{chatSessionId}' was not found for the current user.");

        var messages = await _messages.GetBySessionAsync(chatSessionId, user.Id, cancellationToken).ConfigureAwait(false);
        return messages.Select(ToResponse).ToList();
    }

    public async Task<bool> RateByLogIdAsync(Guid ragQueryLogId, RatingRequest rating, CancellationToken cancellationToken)
    {
        EnsurePersistenceAvailable();
        ValidateRating(rating);
        var user = await _currentUser.GetOrCreateCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        return await _queryLogs
            .UpdateRatingAsync(ragQueryLogId, user.Id, rating.Rating, rating.Feedback, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> RateByMessageAsync(Guid messageId, RatingRequest rating, CancellationToken cancellationToken)
    {
        EnsurePersistenceAvailable();
        ValidateRating(rating);
        var user = await _currentUser.GetOrCreateCurrentUserAsync(cancellationToken).ConfigureAwait(false);

        var logId = await _queryLogs.ResolveLogIdByMessageAsync(messageId, user.Id, cancellationToken).ConfigureAwait(false);
        if (logId is null)
            return false;

        return await _queryLogs
            .UpdateRatingAsync(logId.Value, user.Id, rating.Rating, rating.Feedback, cancellationToken)
            .ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolves each candidate's stable chunk id to its SQL UUIDs and inserts the
    /// retrieved-chunk rows. Failures here are logged and swallowed — the answer
    /// has already been produced and persisted, so logging must not break it.
    /// </summary>
    private async Task SafeLogRetrievedChunksAsync(
        Guid queryLogId, IReadOnlyList<RetrievalCandidate> candidates, CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
            return;

        try
        {
            var stableIds = candidates.Select(c => c.ChunkId).ToList();
            var resolved = await _chunkResolver.ResolveAsync(stableIds, cancellationToken).ConfigureAwait(false);

            var entries = new List<RagRetrievedChunkLogEntry>(candidates.Count);
            var unresolved = 0;
            foreach (var candidate in candidates)
            {
                if (!resolved.TryGetValue(candidate.ChunkId, out var reference))
                {
                    unresolved++;
                    continue; // chunk not (yet) in the SQL store — skip + warn below
                }

                entries.Add(new RagRetrievedChunkLogEntry(
                    Rank: candidate.Rank,
                    DocumentId: reference.DocumentId,
                    ChunkId: reference.ChunkId,
                    SemanticScore: candidate.SemanticScore,
                    LexicalScore: candidate.LexicalScore,
                    RrfScore: candidate.RrfScore,
                    WasSelectedForContext: candidate.WasSelectedForContext));
            }

            if (unresolved > 0)
            {
                _logger.LogWarning(
                    "Retrieved-chunk logging: {Unresolved}/{Total} candidate(s) could not be resolved to a SQL " +
                    "rag_document_chunks.id and were skipped (rag_query_log {LogId}).",
                    unresolved, candidates.Count, queryLogId);
            }

            var inserted = await _retrievedChunkLogs
                .InsertManyAsync(queryLogId, entries, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Logged {Inserted} retrieved chunk(s) for rag_query_log {LogId}.", inserted, queryLogId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to log retrieved chunks for rag_query_log {LogId}; continuing (answer already produced).",
                queryLogId);
        }
    }

    private async Task SafeMarkFailedAsync(Guid queryLogId, string errorMessage, int durationMs)
    {
        try
        {
            // Use None: the request token may already be cancelled/faulted.
            await _queryLogs.MarkFailedAsync(queryLogId, errorMessage, durationMs, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark rag_query_log {LogId} as failed.", queryLogId);
        }
    }

    private async Task SafeMarkCancelledAsync(Guid queryLogId, int durationMs)
    {
        try
        {
            await _queryLogs.MarkCancelledAsync(queryLogId, durationMs, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark rag_query_log {LogId} as cancelled.", queryLogId);
        }
    }

    private void EnsurePersistenceAvailable()
    {
        if (!_db.Enabled)
            throw new InvalidOperationException(
                "Chat history is unavailable because PostgreSQL is disabled (Postgres:Enabled=false).");
    }

    private static void ValidateRating(RatingRequest rating)
    {
        ArgumentNullException.ThrowIfNull(rating);

        // DB CHECK allows only NULL, -1 or 1.
        if (rating.Rating is not (null or -1 or 1))
            throw new ArgumentException("Rating must be -1 (bad), 1 (good) or null.", nameof(rating));
    }

    private static string BuildSessionTitle(string question)
    {
        var trimmed = question.Trim();
        if (trimmed.Length == 0)
            return DefaultSessionTitle;

        return trimmed.Length <= SessionTitleMaxLength
            ? trimmed
            : trimmed[..SessionTitleMaxLength].TrimEnd() + "…";
    }

    private static ChatSessionResponse ToResponse(ChatSession session) => new()
    {
        Id = session.Id,
        Title = session.Title,
        CreatedAt = session.CreatedAt,
        UpdatedAt = session.UpdatedAt
    };

    private static ChatMessageResponse ToResponse(ChatMessage message) => new()
    {
        Id = message.Id,
        Role = message.Role,
        Content = message.Content,
        CreatedAt = message.CreatedAt
    };
}

