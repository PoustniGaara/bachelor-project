using Microsoft.AspNetCore.Mvc;
using Use.Application.Service.Models.Requests;
using Use.Application.Service.Models.Responses;
using Use.Application.Service.Services.Chat;

namespace Use.Application.Service.Controllers;

/// <summary>
/// HTTP entry point used by the web frontend to ask documentation questions and
/// browse chat history. A thin adapter: it validates input, delegates to
/// <see cref="IChatApplicationService"/> and maps failures to status codes.
///
/// <para>
/// Authentication is not implemented yet — the current user is resolved from dev
/// headers (or a Development fallback) inside the application service. All reads
/// are scoped to that user.
/// </para>
/// </summary>
[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly IChatApplicationService _chat;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IChatApplicationService chat, ILogger<ChatController> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    /// <summary>POST /api/chat — run the RAG pipeline and persist the conversation.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ChatResponse>> Ask(
        [FromBody] ChatRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { error = "Field 'question' must not be empty." });

        try
        {
            var response = await _chat.AskAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "RAG pipeline failed.");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Downstream HTTP call failed.");
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = "A downstream service is unavailable." });
        }
    }

    /// <summary>GET /api/chat/sessions — list the current user's chat sessions.</summary>
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatSessionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<ChatSessionResponse>>> GetSessions(
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _chat.GetSessionsAsync(cancellationToken));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return ChatHistoryUnavailable(ex);
        }
    }

    /// <summary>POST /api/chat/sessions — create a new empty chat session.</summary>
    [HttpPost("sessions")]
    [ProducesResponseType(typeof(ChatSessionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ChatSessionResponse>> CreateSession(
        [FromBody] CreateChatSessionRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await _chat.CreateSessionAsync(request?.Title, cancellationToken);
            return CreatedAtAction(nameof(GetMessages), new { chatSessionId = session.Id }, session);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return ChatHistoryUnavailable(ex);
        }
    }

    /// <summary>GET /api/chat/sessions/{chatSessionId}/messages — messages of one owned session.</summary>
    [HttpGet("sessions/{chatSessionId:guid}/messages")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatMessageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<ChatMessageResponse>>> GetMessages(
        Guid chatSessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _chat.GetMessagesAsync(chatSessionId, cancellationToken));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return ChatHistoryUnavailable(ex);
        }
    }

    /// <summary>
    /// POST /api/chat/rag-query-logs/{ragQueryLogId}/rating — rate a RAG answer.
    /// Cleanest path: the chat response returns <c>ragQueryLogId</c>.
    /// </summary>
    [HttpPost("rag-query-logs/{ragQueryLogId:guid}/rating")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RateByLog(
        Guid ragQueryLogId,
        [FromBody] RatingRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { error = "A rating body is required." });

        try
        {
            var updated = await _chat.RateByLogIdAsync(ragQueryLogId, request, cancellationToken);
            return updated
                ? NoContent()
                : NotFound(new { error = $"rag_query_log '{ragQueryLogId}' was not found for the current user." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return ChatHistoryUnavailable(ex);
        }
    }

    /// <summary>
    /// POST /api/chat/messages/{messageId}/rating — rate a RAG answer via a chat
    /// message id (user or assistant; see README on assistant-message mapping).
    /// </summary>
    [HttpPost("messages/{messageId:guid}/rating")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RateByMessage(
        Guid messageId,
        [FromBody] RatingRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { error = "A rating body is required." });

        try
        {
            var updated = await _chat.RateByMessageAsync(messageId, request, cancellationToken);
            return updated
                ? NoContent()
                : NotFound(new { error = $"No RAG log could be resolved for message '{messageId}'." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return ChatHistoryUnavailable(ex);
        }
    }

    private ObjectResult ChatHistoryUnavailable(Exception ex)
    {
        _logger.LogWarning(ex, "Chat history endpoint called but PostgreSQL is disabled.");
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
    }
}

