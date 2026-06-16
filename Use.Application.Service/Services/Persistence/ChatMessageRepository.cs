using Npgsql;
using Use.Application.Service.Models.Chat;

namespace Use.Application.Service.Services.Persistence;

/// <inheritdoc cref="IChatMessageRepository"/>
public sealed class ChatMessageRepository : IChatMessageRepository
{
    private const string Columns = "id, chat_session_id, role, content, created_at";

    private readonly IPostgresDataSourceProvider _db;

    public ChatMessageRepository(IPostgresDataSourceProvider db) => _db = db;

    public async Task<ChatMessage> CreateAsync(
        Guid chatSessionId, string role, string content, CancellationToken cancellationToken)
    {
        const string sql = $"""
            INSERT INTO chat_message (chat_session_id, role, content)
            VALUES (@session, @role, @content)
            RETURNING {Columns};
            """;

        await using var cmd = _db.RequireDataSource().CreateCommand(sql);
        cmd.Parameters.AddWithValue("session", chatSessionId);
        cmd.Parameters.AddWithValue("role", role);
        cmd.Parameters.AddWithValue("content", content);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return Map(reader);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetBySessionAsync(
        Guid chatSessionId, Guid userId, CancellationToken cancellationToken)
    {
        // The JOIN enforces ownership: a session belonging to another user yields
        // no rows, so messages never leak across users.
        const string sql = $"""
            SELECT m.id, m.chat_session_id, m.role, m.content, m.created_at
            FROM chat_message m
            JOIN chat_session s ON s.id = m.chat_session_id
            WHERE m.chat_session_id = @session AND s.user_id = @user
            ORDER BY m.created_at ASC, m.id ASC;
            """;

        await using var cmd = _db.RequireDataSource().CreateCommand(sql);
        cmd.Parameters.AddWithValue("session", chatSessionId);
        cmd.Parameters.AddWithValue("user", userId);

        var messages = new List<ChatMessage>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            messages.Add(Map(reader));
        return messages;
    }

    private static ChatMessage Map(NpgsqlDataReader reader) => new(
        Id: reader.GetGuid(0),
        ChatSessionId: reader.GetGuid(1),
        Role: reader.GetString(2),
        Content: reader.GetString(3),
        CreatedAt: reader.GetFieldValue<DateTimeOffset>(4));
}

