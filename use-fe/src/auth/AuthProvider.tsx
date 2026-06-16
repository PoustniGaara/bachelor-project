import type { ReactNode } from 'react'
import { AuthContext, type AuthContextValue } from './authContext'
import { AUTH_MODE, isMsalConfigured } from './authConfig'
import DevAuthProvider from './DevAuthProvider'
import MsalAuthProvider from './MsalAuthProvider'

const MSAL_CONFIG_ERROR =
  'Microsoft prihlásenie nie je nakonfigurované. Doplňte VITE_MSAL_CLIENT_ID a ' +
  'VITE_MSAL_TENANT_ID do .env.local (alebo prepnite VITE_AUTH_MODE=dev).'

/** Provides a non-crashing context when msal mode is selected but unconfigured. */
function MsalConfigErrorProvider({ children }: { children: ReactNode }) {
  const value: AuthContextValue = {
    isAuthenticated: false,
    isLoading: false,
    user: null,
    mode: 'msal',
    login: () => {},
    logout: () => {},
    configError: MSAL_CONFIG_ERROR,
  }
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

/**
 * Root auth provider. Picks the implementation from `VITE_AUTH_MODE`:
 * - 'dev'  → header-identity dev auth (default)
 * - 'msal' → Microsoft Entra ID (only when configured; otherwise a clear error)
 */
export default function AuthProvider({ children }: { children: ReactNode }) {
  if (AUTH_MODE === 'msal') {
    return isMsalConfigured ? (
      <MsalAuthProvider>{children}</MsalAuthProvider>
    ) : (
      <MsalConfigErrorProvider>{children}</MsalConfigErrorProvider>
    )
  }

  return <DevAuthProvider>{children}</DevAuthProvider>
}

