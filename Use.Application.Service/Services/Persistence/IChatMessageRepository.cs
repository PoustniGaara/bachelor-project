using Use.Application.Service.Models.Chat;

namespace Use.Application.Service.Services.Persistence;

/// <summary>
/// Data access for the <c>chat_message</c> table. Message reads are scoped to a
/// session owned by the given user, so messages can only be loaded from the
/// current user's own sessions.
/// </summary>
public interface IChatMessageRepository
{
    Task<ChatMessage> CreateAsync(Guid chatSessionId, string role, string content, CancellationToken cancellationToken);

    /// <summary>
    /// Messages of a session, oldest first — only if the session belongs to
    /// <paramref name="userId"/> (otherwise an empty list).
    /// </summary>
    Task<IReadOnlyList<ChatMessage>> GetBySessionAsync(Guid chatSessionId, Guid userId, CancellationToken cancellationToken);
}

