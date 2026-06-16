using Npgsql;
using NpgsqlTypes;
using Use.Application.Service.Models.Chat;

namespace Use.Application.Service.Services.Persistence;

/// <summary>
/// Npgsql (direct SQL) implementation of <see cref="IAppUserRepository"/>,
/// matching the lexical-search data-access style (no EF Core).
/// </summary>
public sealed class AppUserRepository : IAppUserRepository
{
    // Column list reused by every SELECT/RETURNING so reads stay in one place.
    private const string Columns =
        "id, entra_object_id, tenant_id, email, display_name, is_active, created_at, last_login_at";

    private readonly IPostgresDataSourceProvider _db;

    public AppUserRepository(IPostgresDataSourceProvider db) => _db = db;

    public async Task<AppUser?> GetByEntraAsync(
        string tenantId, string entraObjectId, CancellationToken cancellationToken)
    {
        const string sql = $"""
            SELECT {Columns}
            FROM app_user
            WHERE tenant_id = @tenant AND entra_object_id = @entra;
            """;

        await using var cmd = _db.RequireDataSource().CreateCommand(sql);
        cmd.Parameters.AddWithValue("tenant", tenantId);
        cmd.Parameters.AddWithValue("entra", entraObjectId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task<AppUser> CreateAsync(
        string tenantId, string entraObjectId, string email, string? displayName, CancellationToken cancellationToken)
    {
        const string sql = $"""
            INSERT INTO app_user (entra_object_id, tenant_id, email, display_name, last_login_at)
            VALUES (@entra, @tenant, @email, @display, now())
            RETURNING {Columns};
            """;

        await using var cmd = _db.RequireDataSource().CreateCommand(sql);
        AddIdentityParameters(cmd, tenantId, entraObjectId, email, displayName);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return Map(reader);
    }

    public async Task UpdateLastLoginAsync(Guid userId, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE app_user SET last_login_at = now() WHERE id = @id;";

        await using var cmd = _db.RequireDataSource().CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppUser> GetOrCreateAsync(
        string tenantId, string entraObjectId, string email, string? displayName, CancellationToken cancellationToken)
    {
        // One atomic upsert keyed on (tenant_id, entra_object_id): create the user
        // on first sight, otherwise refresh last_login_at + the latest profile
        // fields from the identity provider.
        const string sql = $"""
            INSERT INTO app_user (entra_object_id, tenant_id, email, display_name, last_login_at)
            VALUES (@entra, @tenant, @email, @display, now())
            ON CONFLICT (tenant_id, entra_object_id) DO UPDATE
                SET last_login_at = now(),
                    email = EXCLUDED.email,
                    display_name = COALESCE(EXCLUDED.display_name, app_user.display_name)
            RETURNING {Columns};
            """;

        await using var cmd = _db.RequireDataSource().CreateCommand(sql);
        AddIdentityParameters(cmd, tenantId, entraObjectId, email, displayName);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return Map(reader);
    }

    private static void AddIdentityParameters(
        NpgsqlCommand cmd, string tenantId, string entraObjectId, string email, string? displayName)
    {
        cmd.Parameters.AddWithValue("entra", entraObjectId);
        cmd.Parameters.AddWithValue("tenant", tenantId);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.Add(new NpgsqlParameter("display", NpgsqlDbType.Text)
        {
            Value = (object?)displayName ?? DBNull.Value
        });
    }

    private static AppUser Map(NpgsqlDataReader reader) => new(
        Id: reader.GetGuid(0),
        EntraObjectId: reader.GetString(1),
        TenantId: reader.GetString(2),
        Email: reader.GetString(3),
        DisplayName: reader.IsDBNull(4) ? null : reader.GetString(4),
        IsActive: reader.GetBoolean(5),
        CreatedAt: reader.GetFieldValue<DateTimeOffset>(6),
        LastLoginAt: reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7));
}

