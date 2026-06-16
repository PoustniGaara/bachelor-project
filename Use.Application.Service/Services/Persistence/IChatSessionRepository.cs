using Use.Application.Service.Models.Chat;

namespace Use.Application.Service.Services.Persistence;

/// <summary>
/// Data access for the <c>chat_session</c> table. Every read is scoped by
/// <c>userId</c> so one user can never see another user's sessions.
/// </summary>
public interface IChatSessionRepository
{
    Task<ChatSession> CreateAsync(Guid userId, string? title, CancellationToken cancellationToken);

    /// <summary>All sessions for a user, newest first.</summary>
    Task<IReadOnlyList<ChatSession>> GetByUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>One session, only if it belongs to <paramref name="userId"/> (else null).</summary>
    Task<ChatSession?> GetByIdAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Set <c>updated_at = now()</c> for a session owned by the user. Returns true when a row changed.</summary>
    Task<bool> TouchAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Rename a session owned by the user. Returns true when a row changed.</summary>
    Task<bool> UpdateTitleAsync(Guid sessionId, Guid userId, string? title, CancellationToken cancellationToken);
}

