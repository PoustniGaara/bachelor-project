using Npgsql;
using NpgsqlTypes;
using Use.Application.Service.Models.Chat;

namespace Use.Application.Service.Services.Persistence;

/// <inheritdoc cref="IChatSessionRepository"/>
public sealed class ChatSessionRepository : IChatSessionRepository
{
    private const string Columns = "id, user_id, title, created_at, updated_at";

    private readonly IPostgresDataSourceProvider _db;

    public ChatSessionRepository(IPostgresDataSourceProvider db) => _db = db;

    public async Task<ChatSession> CreateAsync(Guid userId, string? title, CancellationToken cancellationToken)
    {
        const string sql = $"""
            INSERT INTO chat_session (user_id, title)
            VALUES (@user, @title)
            RETURNING {Columns};
            """;

        await using var cmd = _db.RequireDataSource().CreateCommand(sql);
        cmd.Parameters.AddWithValue("user", userId);
        cmd.Parameters.Add(new NpgsqlParameter("title", NpgsqlDbType.Text)
        {
            Value = (object?)title ?? DBNull.Value
        });

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return Map(reader);
    }

    public async Task<IReadOnlyList<ChatSession>> GetByUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT {Columns}
            FROM chat_session
            WHERE user_id = @user
            ORDER BY created_at DESC;
            """;

        await using var cmd = _db.RequireDataSource().CreateCommand(sql);
        cmd.Parameters.AddWithValue("user", userId);

        var sessions = new List<ChatSession>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            sessions.Add(Map(reader));
        return sessions;
    }

    public async Task<ChatSession?> GetByIdAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT {Columns}
            FROM chat_session
            WHERE id = @id AND user_id = @user;
            """;

        await using var cmd = _db.RequireDataSource().CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", sessionId);
        cmd.Parameters.AddWithValue("user", userId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task<bool> TouchAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE chat_session SET updated_at = now() WHERE id = @id AND user_id = @user;";

        await using var cmd = _db.RequireDataSource().CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", sessionId);
        cmd.Parameters.AddWithValue("user", userId);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<bool> UpdateTitleAsync(Guid sessionId, Guid userId, string? title, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE chat_session SET title = @title, updated_at = now() WHERE id = @id AND user_id = @user;";

        await using var cmd = _db.RequireDataSource().CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", sessionId);
        cmd.Parameters.AddWithValue("user", userId);
        cmd.Parameters.Add(new NpgsqlParameter("title", NpgsqlDbType.Text)
        {
            Value = (object?)title ?? DBNull.Value
        });
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    private static ChatSession Map(NpgsqlDataReader reader) => new(
        Id: reader.GetGuid(0),
        UserId: reader.GetGuid(1),
        Title: reader.IsDBNull(2) ? null : reader.GetString(2),
        CreatedAt: reader.GetFieldValue<DateTimeOffset>(3),
        UpdatedAt: reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4));
}

