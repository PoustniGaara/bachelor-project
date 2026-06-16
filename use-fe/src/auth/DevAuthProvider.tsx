import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { AuthContext, type AuthContextValue } from './authContext'
import { devIdentity, getInitials } from './authConfig'
import { registerAuthHeaderProvider } from '../services/apiClient'
import type { CurrentUser } from '../types/chat'

const DEV_AUTH_KEY = 'use-fe.dev-auth'

/**
 * Development auth provider. No real identity: the user is "signed in" after a
 * one-click dev login (persisted in localStorage so refresh keeps the session),
 * and API calls carry `X-User-*` identity headers matching the backend's dev
 * identity. Swap `VITE_AUTH_MODE=msal` for real Microsoft Entra ID.
 */
export default function DevAuthProvider({ children }: { children: ReactNode }) {
  const [isAuthenticated, setIsAuthenticated] = useState<boolean>(
    () => localStorage.getItem(DEV_AUTH_KEY) === '1',
  )

  const user = useMemo<CurrentUser>(
    () => ({
      name: devIdentity.name,
      email: devIdentity.email,
      initials: getInitials(devIdentity.name, devIdentity.email),
    }),
    [],
  )

  const login = useCallback(() => {
    localStorage.setItem(DEV_AUTH_KEY, '1')
    setIsAuthenticated(true)
  }, [])

  const logout = useCallback(() => {
    localStorage.removeItem(DEV_AUTH_KEY)
    setIsAuthenticated(false)
  }, [])

  // Attach dev identity headers to every API call.
  useEffect(() => {
    registerAuthHeaderProvider(async () => ({
      'X-User-Email': devIdentity.email,
      'X-User-Name': devIdentity.name,
      'X-Entra-Object-Id': devIdentity.entraObjectId,
      'X-Tenant-Id': devIdentity.tenantId,
    }))
  }, [])

  const value: AuthContextValue = {
    isAuthenticated,
    isLoading: false,
    user: isAuthenticated ? user : null,
    mode: 'dev',
    login,
    logout,
    configError: null,
  }

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

