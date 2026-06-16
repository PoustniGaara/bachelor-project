/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** 'dev' (header identity) or 'msal' (Microsoft Entra ID). Defaults to 'dev'. */
  readonly VITE_AUTH_MODE?: 'dev' | 'msal'

  // Dev identity headers (VITE_AUTH_MODE=dev). Sent as X-User-* request headers.
  readonly VITE_DEV_USER_EMAIL?: string
  readonly VITE_DEV_USER_NAME?: string
  readonly VITE_DEV_ENTRA_OBJECT_ID?: string
  readonly VITE_DEV_TENANT_ID?: string

  // Microsoft Entra ID / MSAL (VITE_AUTH_MODE=msal).
  readonly VITE_MSAL_CLIENT_ID?: string
  readonly VITE_MSAL_TENANT_ID?: string
  readonly VITE_MSAL_REDIRECT_URI?: string
  /** Backend API scope, e.g. api://<backend-client-id>/access_as_user. */
  readonly VITE_API_SCOPE?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}

