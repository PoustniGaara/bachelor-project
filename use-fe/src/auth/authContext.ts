import { createContext } from 'react'
import type { AuthMode } from './authConfig'
import type { CurrentUser } from '../types/chat'

export type AuthContextValue = {
  /** True when a user is signed in. */
  isAuthenticated: boolean
  /** True while auth state is still being determined (MSAL init / redirect). */
  isLoading: boolean
  /** The signed-in user, or null. */
  user: CurrentUser | null
  /** Active auth mode ('dev' | 'msal'). */
  mode: AuthMode
  /** Start sign-in (dev: local flag; msal: redirect to Microsoft). */
  login: () => void | Promise<void>
  /** Sign out (dev: clear flag; msal: redirect logout). */
  logout: () => void | Promise<void>
  /** Set when 'msal' mode is selected but not configured (so the UI can explain). */
  configError: string | null
}

export const AuthContext = createContext<AuthContextValue | undefined>(undefined)

