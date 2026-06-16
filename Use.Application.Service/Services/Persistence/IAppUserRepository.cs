using Use.Application.Service.Models.Chat;

namespace Use.Application.Service.Services.Persistence;

/// <summary>
/// Data access for the <c>app_user</c> table. No passwords are stored — identity
/// comes from Microsoft Entra (later) or a dev/header identity (now).
/// </summary>
public interface IAppUserRepository
{
    /// <summary>Look up a user by its Entra identity (tenant + object id).</summary>
    Task<AppUser?> GetByEntraAsync(string tenantId, string entraObjectId, CancellationToken cancellationToken);

    /// <summary>Insert a new user (sets <c>last_login_at = now()</c>).</summary>
    Task<AppUser> CreateAsync(
        string tenantId, string entraObjectId, string email, string? displayName, CancellationToken cancellationToken);

    /// <summary>Stamp <c>last_login_at = now()</c> for an existing user.</summary>
    Task UpdateLastLoginAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically get-or-create the user by Entra identity and stamp the login
    /// time. Returns the resolved row.
    /// </summary>
    Task<AppUser> GetOrCreateAsync(
        string tenantId, string entraObjectId, string email, string? displayName, CancellationToken cancellationToken);
}

