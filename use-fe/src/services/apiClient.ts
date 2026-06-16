import type { ChatErrorBody } from '../types/chat'

/**
 * Error carrying the HTTP status so callers (and the UI) can react to
 * 400/401/403/404/502/503 specifically.
 */
export class ApiError extends Error {
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

/**
 * Produces the auth headers attached to every API call. Registered once by the
 * auth layer (`AuthProvider`) so request code never duplicates header logic:
 * - dev mode  → `X-User-*` identity headers
 * - msal mode → `Authorization: Bearer <token>`
 */
export type AuthHeaderProvider = () => Promise<Record<string, string>>

let authHeaderProvider: AuthHeaderProvider = async () => ({})

/** Called by the auth layer to wire in the current header strategy. */
export function registerAuthHeaderProvider(provider: AuthHeaderProvider): void {
  authHeaderProvider = provider
}

type RequestOptions = {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  body?: unknown
  signal?: AbortSignal
}

async function buildHeaders(hasBody: boolean): Promise<Record<string, string>> {
  let authHeaders: Record<string, string> = {}
  try {
    authHeaders = await authHeaderProvider()
  } catch {
    // Token acquisition can fail (e.g. silent renew) — fall through unauthenticated;
    // the backend will answer 401 and the UI will surface it.
  }

  return {
    ...(hasBody ? { 'Content-Type': 'application/json' } : {}),
    ...authHeaders,
  }
}

/**
 * Central fetch wrapper. Uses relative `/api/...` paths (Vite proxy), attaches
 * auth headers, parses `{ error }` bodies into an {@link ApiError}, and handles
 * empty (`204`) responses.
 */
export async function apiRequest<T>(
  path: string,
  { method = 'GET', body, signal }: RequestOptions = {},
): Promise<T> {
  const hasBody = body !== undefined
  const headers = await buildHeaders(hasBody)

  const response = await fetch(path, {
    method,
    headers,
    body: hasBody ? JSON.stringify(body) : undefined,
    signal,
  })

  if (!response.ok) {
    let message = `Request failed with status ${response.status}`
    try {
      const errorBody = (await response.json()) as ChatErrorBody
      if (errorBody?.error) message = errorBody.error
    } catch {
      // response wasn't JSON — keep the generic message
    }
    throw new ApiError(message, response.status)
  }

  if (response.status === 204) {
    return undefined as T
  }

  const text = await response.text()
  return (text ? (JSON.parse(text) as T) : (undefined as T))
}

