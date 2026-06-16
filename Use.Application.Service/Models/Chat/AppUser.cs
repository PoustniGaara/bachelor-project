namespace Use.Application.Service.Models.Chat;

/// <summary>
/// One application user. Mirrors the <c>app_user</c> table.
///
/// <para>
/// Authentication is <b>not</b> implemented yet. Microsoft Entra ID / JWT
/// extraction will later populate <see cref="EntraObjectId"/> /
/// <see cref="TenantId"/> from the validated token; for now a dev/header-based
/// identity is resolved by <c>ICurrentUserService</c>. No passwords are stored.
/// </para>
/// </summary>
public sealed record AppUser(
    Guid Id,
    string EntraObjectId,
    string TenantId,
    string Email,
    string? DisplayName,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

