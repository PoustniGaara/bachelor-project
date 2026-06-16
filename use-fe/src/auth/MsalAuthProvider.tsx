import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import {
  InteractionStatus,
  PublicClientApplication,
  type AccountInfo,
} from '@azure/msal-browser'
import { MsalProvider, useIsAuthenticated, useMsal } from '@azure/msal-react'
import { AuthContext, type AuthContextValue } from './authContext'
import { apiTokenScopes, getInitials, loginRequest, msalConfig } from './authConfig'
import { registerAuthHeaderProvider } from '../services/apiClient'
import type { CurrentUser } from '../types/chat'

function toUser(account: AccountInfo | null): CurrentUser | null {
  if (!account) return null
  const name = account.name ?? account.username
  const email = account.username
  return { name, email, initials: getInitials(name, email) }
}

/** Inner provider — runs inside <MsalProvider>, so MSAL hooks are available. */
function MsalAuthInner({ children }: { children: ReactNode }) {
  const { instance, accounts, inProgress } = useMsal()
  const isAuthenticated = useIsAuthenticated()

  const account = accounts[0] ?? null
  const user = useMemo(() => toUser(account), [account])

  const login = useCallback(() => {
    void instance.loginRedirect(loginRequest)
  }, [instance])

  const logout = useCallback(() => {
    void instance.logoutRedirect()
  }, [instance])

  // Attach a bearer token (silently acquired for the backend API scope) to calls.
  useEffect(() => {
    registerAuthHeaderProvider(async () => {
      const activeAccount = instance.getActiveAccount() ?? accounts[0] ?? null
      if (!activeAccount || apiTokenScopes.length === 0) return {}

      const result = await instance.acquireTokenSilent({
        scopes: apiTokenScopes,
        account: activeAccount,
      })
      const headers: Record<string, string> = { Authorization: `Bearer ${result.accessToken}` }
      return headers
    })
  }, [instance, accounts])

  const value: AuthContextValue = {
    isAuthenticated,
    isLoading: inProgress !== InteractionStatus.None,
    user: isAuthenticated ? user : null,
    mode: 'msal',
    login,
    logout,
    configError: null,
  }

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

/**
 * Microsoft Entra ID auth provider. Creates the MSAL instance, runs
 * initialize + redirect handling, then renders <MsalProvider>. Only mounted when
 * MSAL is configured (see AuthProvider).
 */
export default function MsalAuthProvider({ children }: { children: ReactNode }) {
  const [instance] = useState(() => new PublicClientApplication(msalConfig))
  const [ready, setReady] = useState(false)

  useEffect(() => {
    let active = true

    instance
      .initialize()
      .then(() => instance.handleRedirectPromise())
      .then((result) => {
        const account = result?.account ?? instance.getActiveAccount() ?? instance.getAllAccounts()[0]
        if (account) instance.setActiveAccount(account)
      })
      .catch((err) => {
        console.error('MSAL initialization failed', err)
      })
      .finally(() => {
        if (active) setReady(true)
      })

    return () => {
      active = false
    }
  }, [instance])

  if (!ready) {
    const loadingValue: AuthContextValue = {
      isAuthenticated: false,
      isLoading: true,
      user: null,
      mode: 'msal',
      login: () => {},
      logout: () => {},
      configError: null,
    }
    return <AuthContext.Provider value={loadingValue}>{children}</AuthContext.Provider>
  }

  return (
    <MsalProvider instance={instance}>
      <MsalAuthInner>{children}</MsalAuthInner>
    </MsalProvider>
  )
}

