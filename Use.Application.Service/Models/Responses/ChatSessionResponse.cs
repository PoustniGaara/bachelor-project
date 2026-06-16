namespace Use.Application.Service.Models.Responses;

/// <summary>
/// One chat session in the current user's history list
/// (<c>GET /api/chat/sessions</c>).
/// </summary>
public sealed class ChatSessionResponse
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

