namespace Use.Application.Service.Models.Responses;

/// <summary>
/// One message in a session
/// (<c>GET /api/chat/sessions/{chatSessionId}/messages</c>).
/// </summary>
public sealed class ChatMessageResponse
{
    public Guid Id { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

