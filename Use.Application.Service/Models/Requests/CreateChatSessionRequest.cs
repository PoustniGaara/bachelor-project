namespace Use.Application.Service.Models.Requests;

/// <summary>
/// Body of <c>POST /api/chat/sessions</c> — create an empty chat session.
/// Optional: <c>POST /api/chat</c> can auto-create a session when none is given.
/// </summary>
public sealed class CreateChatSessionRequest
{
    /// <summary>Optional title. Defaults to "New chat" when omitted.</summary>
    public string? Title { get; set; }
}

