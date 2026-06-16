namespace Use.Application.Service.Models.Chat;

/// <summary>
/// One message inside a <see cref="ChatSession"/>.
/// Mirrors the <c>chat_message</c> table.
/// </summary>
public sealed record ChatMessage(
    Guid Id,
    Guid ChatSessionId,
    string Role,
    string Content,
    DateTimeOffset CreatedAt);

/// <summary>
/// Allowed values for <see cref="ChatMessage.Role"/>. Must stay in sync with the
/// <c>chk_chat_message_role</c> CHECK constraint in <c>create.sql</c>.
/// </summary>
public static class ChatMessageRole
{
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string System = "system";
}

