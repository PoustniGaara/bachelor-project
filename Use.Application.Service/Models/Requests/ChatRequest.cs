using System.ComponentModel.DataAnnotations;

namespace Use.Application.Service.Models.Requests;

/// <summary>
/// Body of <c>POST /api/chat</c> — a question typed by the end user
/// in the web UI.
/// </summary>
public sealed class ChatRequest
{
    /// <summary>The user's natural-language question.</summary>
    [Required]
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Existing chat session to continue. When <c>null</c> the service creates a
    /// new session for the current user and returns its id on the response.
    /// </summary>
    public Guid? ChatSessionId { get; set; }

    // TODO: add user identity / permission filters once JWT validation is in place.
    // TODO: feed prior chat_message history into the prompt builder (currently
    //       persisted but not yet replayed into the prompt).
}

