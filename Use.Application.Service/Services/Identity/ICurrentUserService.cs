using Use.Application.Service.Models.Chat;

namespace Use.Application.Service.Services.Identity;

/// <summary>
/// Resolves the current application user for the in-flight request.
///
/// <para>
/// <b>Authentication is not implemented yet.</b> This abstraction is the single
/// seam where Microsoft Entra ID / JWT identity extraction will later plug in
/// (read validated claims from <c>HttpContext.User</c>). For now it resolves the
/// caller from dev headers, or — in Development only — a deterministic local dev
/// user. The resolved user is persisted in / read from <c>app_user</c>.
/// </para>
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// Resolve the caller's identity, upsert it into <c>app_user</c> (stamping
    /// <c>last_login_at</c>), and return the row.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">
    /// No identity could be resolved outside Development.
    /// </exception>
    Task<AppUser> GetOrCreateCurrentUserAsync(CancellationToken cancellationToken);
}

