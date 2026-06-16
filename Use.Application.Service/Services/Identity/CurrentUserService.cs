using Use.Application.Service.Models.Chat;
using Use.Application.Service.Services.Persistence;

namespace Use.Application.Service.Services.Identity;

/// <summary>
/// Dev/header-based <see cref="ICurrentUserService"/>. Resolution order:
/// <list type="number">
/// <item>Identity headers (<c>X-Entra-Object-Id</c> / <c>X-User-Email</c> / …).</item>
/// <item>In Development only: a deterministic local dev user.</item>
/// <item>Otherwise: <see cref="UnauthorizedAccessException"/> (→ 401).</item>
/// </list>
///
/// <para>
/// TODO (auth): replace header parsing with claims read from the validated
/// Microsoft Entra ID JWT on <c>HttpContext.User</c> (<c>oid</c>, <c>tid</c>,
/// <c>preferred_username</c>/<c>email</c>, <c>name</c>). The rest of the pipeline
/// already depends only on this interface, so no downstream change is required.
/// </para>
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    // Temporary dev identity headers (replaced by JWT claims later).
    private const string EntraObjectIdHeader = "X-Entra-Object-Id";
    private const string TenantIdHeader = "X-Tenant-Id";
    private const string EmailHeader = "X-User-Email";
    private const string DisplayNameHeader = "X-User-Name";

    // Deterministic local dev user (Development only, when no identity headers).
    private const string DevEntraObjectId = "local-dev-user";
    private const string DevTenantId = "local-dev-tenant";
    private const string DevEmail = "local.dev@use.local";
    private const string DevDisplayName = "Local Dev User";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAppUserRepository _users;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<CurrentUserService> _logger;

    public CurrentUserService(
        IHttpContextAccessor httpContextAccessor,
        IAppUserRepository users,
        IHostEnvironment environment,
        ILogger<CurrentUserService> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _users = users;
        _environment = environment;
        _logger = logger;
    }

    public async Task<AppUser> GetOrCreateCurrentUserAsync(CancellationToken cancellationToken)
    {
        var identity = ResolveIdentity();

        return await _users.GetOrCreateAsync(
            identity.TenantId, identity.EntraObjectId, identity.Email, identity.DisplayName, cancellationToken)
            .ConfigureAwait(false);
    }

    private ResolvedIdentity ResolveIdentity()
    {
        if (TryResolveFromHeaders(out var headerIdentity))
        {
            _logger.LogDebug("Resolved current user from identity headers ({Email}).", headerIdentity!.Email);
            return headerIdentity;
        }

        if (_environment.IsDevelopment())
        {
            _logger.LogDebug("No identity headers present — falling back to the local dev user (Development).");
            return new ResolvedIdentity(DevEntraObjectId, DevTenantId, DevEmail, DevDisplayName);
        }

        // Outside Development a missing identity is treated as unauthorized.
        throw new UnauthorizedAccessException(
            "No authenticated identity could be resolved for the request.");
    }

    private bool TryResolveFromHeaders(out ResolvedIdentity? identity)
    {
        identity = null;

        var headers = _httpContextAccessor.HttpContext?.Request.Headers;
        if (headers is null)
            return false;

        var entra = Trimmed(headers[EntraObjectIdHeader]);
        var email = Trimmed(headers[EmailHeader]);
        var tenant = Trimmed(headers[TenantIdHeader]);
        var displayName = Trimmed(headers[DisplayNameHeader]);

        // We need at least an Entra object id or an email to consider the request
        // header-identified; the remaining required fields are derived safely.
        if (string.IsNullOrEmpty(entra) && string.IsNullOrEmpty(email))
            return false;

        email ??= entra is not null ? $"{entra}@use.local" : "unknown@use.local";
        entra ??= email; // email is a stable enough object id until real auth lands
        tenant ??= DevTenantId;

        identity = new ResolvedIdentity(entra, tenant, email, displayName);
        return true;
    }

    private static string? Trimmed(Microsoft.Extensions.Primitives.StringValues value)
    {
        var s = value.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private sealed record ResolvedIdentity(string EntraObjectId, string TenantId, string Email, string? DisplayName);
}

