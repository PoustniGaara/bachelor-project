namespace Use.Application.Service.Models.Chat;

/// <summary>
/// One conversation thread owned by an <see cref="AppUser"/>.
/// Mirrors the <c>chat_session</c> table.
/// </summary>
public sealed record ChatSession(
    Guid Id,
    Guid UserId,
    string? Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

