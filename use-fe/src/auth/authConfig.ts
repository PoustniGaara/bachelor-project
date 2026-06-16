import type { Configuration, PopupRequest, RedirectRequest } from '@azure/msal-browser'

export type AuthMode = 'dev' | 'msal'

/** Selected auth mode. Anything other than 'msal' is treated as 'dev'. */
export const AUTH_MODE: AuthMode = import.meta.env.VITE_AUTH_MODE === 'msal' ? 'msal' : 'dev'

// ---------------------------------------------------------------------------
// Dev identity (VITE_AUTH_MODE=dev). Sent to the backend as X-User-* headers.
// Defaults intentionally match the backend's Development fallback user.
// ---------------------------------------------------------------------------
export const devIdentity = {
  email: import.meta.env.VITE_DEV_USER_EMAIL ?? 'local.dev@use.local',
  name: import.meta.env.VITE_DEV_USER_NAME ?? 'Local Dev User',
  entraObjectId: import.meta.env.VITE_DEV_ENTRA_OBJECT_ID ?? 'local-dev-user',
  tenantId: import.meta.env.VITE_DEV_TENANT_ID ?? 'local-dev-tenant',
}

// ---------------------------------------------------------------------------
// Microsoft Entra ID / MSAL (VITE_AUTH_MODE=msal).
// ---------------------------------------------------------------------------
const msalClientId = import.meta.env.VITE_MSAL_CLIENT_ID ?? ''
const msalTenantId = import.meta.env.VITE_MSAL_TENANT_ID ?? ''
const msalRedirectUri = import.meta.env.VITE_MSAL_REDIRECT_URI ?? window.location.origin

/** Backend API scope requested for the bearer token (api://<client-id>/access_as_user). */
export const apiScope = import.meta.env.VITE_API_SCOPE ?? ''

/** True only when the minimum MSAL values are present. */
export const isMsalConfigured = Boolean(msalClientId && msalTenantId)

export const msalConfig: Configuration = {
  auth: {
    clientId: msalClientId,
    authority: `https://login.microsoftonline.com/${msalTenantId || 'common'}`,
    redirectUri: msalRedirectUri,
    postLogoutRedirectUri: msalRedirectUri,
  },
  cache: {
    cacheLocation: 'sessionStorage',
  },
}

/** Scopes used at interactive login. */
export const loginRequest: RedirectRequest & PopupRequest = {
  scopes: apiScope ? [apiScope] : ['openid', 'profile', 'email'],
}

/** Scopes used when silently acquiring the backend API token. */
export const apiTokenScopes: string[] = apiScope ? [apiScope] : []

/** Two-line initials from a display name (falls back to the email local part). */
export function getInitials(name?: string | null, email?: string | null): string {
  const source = (name && name.trim()) || (email ? email.split('@')[0] : '') || '?'
  const parts = source.split(/[\s._-]+/).filter(Boolean)
  if (parts.length === 0) return '?'
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
}

